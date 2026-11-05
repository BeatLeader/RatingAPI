# Retrains tagging_model.onnx (InferPublish.Tag: [acc, tech, pass] -> a map category) from the maps hand labeled on
# BeatLeader, following BeatLeader's "Map Categories" Deepnote notebook:
# - labels: the category bits of each ranked leaderboard's difficulty.type on the BeatLeader API (acc 1, tech 2,
#   midspeed 4, speed 8, extreme --extreme-bit; the other bits are traits: fitbeat 16, linear 32, bomb reset 64). A map
#   with several categories gets the highest one, like the notebook. --labels-csv (columns Id,Label) adds or overrides
#   labels, e.g. Extreme tags not on BeatLeader yet.
# - only manual labels: since AUTO_TAG_START BeatLeader tags maps with this model without recording a change (at
#   nomination, RatingUtils.UpdateTags, and a one-off run over the untagged maps, most of those nominated in 2025). A map
#   only counts if the latest change setting its category explicitly (leaderboard or qualification changes: any before
#   AUTO_TAG_START with a category, an RT edit or a reweight that changed it since) set the current category.
#   --include-auto keeps the model's own tags.
# - features: acc, tech and pass ratings, from --features-db (the database the model will tag, so the boundaries follow
#   its rating scale) or, without it, the API's ratings like the notebook.
# - model: 70/15/15 stratified train/val/test split, the notebook's candidates (KNN k 1-50, SVC linear / poly 1-5 / rbf,
#   random forest and gradient boosting 1-100 trees), the best balanced accuracy on val wins (--family picks the best of
#   one family), trained on train like the notebook. The current tagging_model.onnx is a linear SVC.
#
# Usage (from the repository root):
#   python RatingAPI/Training/train_tagging_model.py --features-db wwwroot/Database.db
# Writes RatingAPI/Training/tagging_model_new.onnx (gitignored); copy it over RatingAPI/tagging_model.onnx once the report
# looks right, then rebuild RatingAPI and run the tags refresh. The API labels are cached in labeled_maps.csv (gitignored),
# --refresh downloads them again.
import argparse, csv, os, sqlite3, sys, time, warnings
from concurrent.futures import ThreadPoolExecutor
import numpy as np
import onnxruntime as ort
import requests
from sklearn.ensemble import GradientBoostingClassifier, RandomForestClassifier
from sklearn.metrics import balanced_accuracy_score, confusion_matrix
from sklearn.model_selection import train_test_split
from sklearn.neighbors import KNeighborsClassifier
from sklearn.svm import SVC
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType

warnings.filterwarnings('ignore', category=FutureWarning)

HERE = os.path.dirname(os.path.abspath(__file__))
CACHE = os.path.join(HERE, 'labeled_maps.csv')
API = 'https://api.beatleader.xyz/leaderboards?page={}&count=100&type=ranked&sortBy=timestamp&order=desc'
LEADERBOARD_API = 'https://api.beatleader.xyz/leaderboard/{}?count=0'
# beatleader-server 2658529 (2025-11-17 03:40 UTC) added RatingUtils.UpdateTags to NominateMap; nothing set the type
# without recording a change before
AUTO_TAG_START = 1763350840
CATEGORY_MASK = 1 | 2 | 4 | 8 | 128


def get_json(url):
    for attempt in range(10):
        try:
            return requests.get(url, timeout=30).json()
        except Exception:
            time.sleep(1 + attempt)
    sys.exit(f'could not download {url}')


def manual_type(id_, type_):
    # Whether a recorded change set the current category (the auto tagging records none). Since AUTO_TAG_START a change
    # keeping the category may only resubmit the auto tag, so only the ones changing it count.
    lb = get_json(LEADERBOARD_API.format(id_))
    changes = (lb.get('changes') or []) + ((lb.get('qualification') or {}).get('changes') or [])
    changes = [c for c in changes if c['newType'] & CATEGORY_MASK and (c['timeset'] < AUTO_TAG_START
               or (c['oldType'] & CATEGORY_MASK) != (c['newType'] & CATEGORY_MASK))]
    if not changes:
        return False
    return (max(changes, key=lambda c: c['timeset'])['newType'] & CATEGORY_MASK) == (type_ & CATEGORY_MASK)


def fetch_maps():
    maps, page = [], 1
    while True:
        data = get_json(API.format(page))
        if not data['data']:
            break
        for m in data['data']:
            d = m['difficulty']
            maps.append({'Id': m['id'], 'Type': d['type'], 'AccRating': d['accRating'] or 0, 'TechRating': d['techRating'] or 0,
                         'PassRating': d['passRating'] or 0, 'Name': f"{m['song']['name']} ({d['difficultyName']}, {d['modeName']})"})
        print(f'\rdownloaded {len(maps)} / {data["metadata"]["total"]}', end='')
        page += 1
    print()
    with ThreadPoolExecutor(8) as pool:
        for k, (m, manual) in enumerate(zip(maps, pool.map(lambda m: manual_type(m['Id'], m['Type']), maps))):
            m['Manual'] = int(manual)
            print(f'\rchecked the tag history of {k + 1} / {len(maps)}', end='')
    print()
    with open(CACHE, 'w', newline='', encoding='utf-8') as f:
        w = csv.DictWriter(f, fieldnames=list(maps[0]))
        w.writeheader()
        w.writerows(maps)
    return maps


def load_maps(refresh):
    if not refresh and os.path.exists(CACHE):
        with open(CACHE, newline='', encoding='utf-8') as f:
            rows = list(csv.DictReader(f))
        if rows and 'Manual' in rows[0]:
            return [{**r, **{k: int(r[k]) for k in ('Type', 'Manual')},
                     **{k: float(r[k]) for k in ('AccRating', 'TechRating', 'PassRating')}} for r in rows]
        print(f'{CACHE} has no tag history, downloading again')
    return fetch_maps()


def label(type_, bits):
    # The highest category bit, like the notebook (speed over midspeed over tech over acc; extreme over all)
    found = [name for name, bit in sorted(bits.items(), key=lambda b: b[1]) if type_ & bit]
    return (found[-1] if found else None), len(found)


def candidates(family):
    c = []
    if family in (None, 'knn'):
        c += [(f'KNN k={k}', KNeighborsClassifier(n_neighbors=k)) for k in range(1, 51)]
    if family in (None, 'svm'):
        c += [('SVC linear', SVC(kernel='linear', random_state=42))]
        c += [(f'SVC poly degree={d}', SVC(kernel='poly', degree=d, random_state=42)) for d in range(1, 6)]
        c += [('SVC rbf', SVC(kernel='rbf', random_state=42))]
    if family in (None, 'rf'):
        c += [(f'random forest {n} trees', RandomForestClassifier(n_estimators=n, random_state=42)) for n in range(1, 101)]
    if family in (None, 'gbt'):
        c += [(f'gradient boosting {n} trees', GradientBoostingClassifier(n_estimators=n, random_state=42)) for n in range(1, 101)]
    return c


def onnx_predict(model_path, x):
    s = ort.InferenceSession(model_path)
    return s.run([s.get_outputs()[0].name], {'float_input': x.astype(np.float32)})[0].astype(str)


def print_confusion(title, rows, truth, pred, classes):
    print(f'\n{title} (rows {rows}, columns new)')
    print(' ' * 10 + ''.join(f'{c:>10}' for c in classes))
    for c, row in zip(classes, confusion_matrix(truth, pred, labels=classes)):
        print(f'{c:>10}' + ''.join(f'{v:10d}' for v in row))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--features-db', help='database whose ratings the model learns and tags (default: the API ratings)')
    ap.add_argument('--labels-csv', help='extra hand labels (columns Id,Label), override the API ones')
    ap.add_argument('--extreme-bit', type=int, default=128, help="difficulty.type bit of Extreme on BeatLeader (0: none)")
    ap.add_argument('--refresh', action='store_true', help='download the labels again')
    ap.add_argument('--include-auto', action='store_true', help="also learn the tags BeatLeader's auto tagging set")
    ap.add_argument('--family', choices=['knn', 'svm', 'rf', 'gbt'], help='only this model family')
    ap.add_argument('--old-model', default=os.path.join(HERE, '..', 'tagging_model.onnx'))
    ap.add_argument('--out', default=os.path.join(HERE, 'tagging_model_new.onnx'))
    ap.add_argument('--changed', type=int, default=25, help='maps that change category to list')
    a = ap.parse_args()

    bits = {'acc': 1, 'tech': 2, 'midspeed': 4, 'speed': 8}
    if a.extreme_bit:
        bits['extreme'] = a.extreme_bit
    maps = {m['Id']: m for m in load_maps(a.refresh)}
    labels, multi, auto = {}, 0, 0
    for i, m in maps.items():
        lab, n = label(m['Type'], bits)
        if lab and not m['Manual'] and not a.include_auto:
            auto += 1
        elif lab:
            labels[i] = lab
            multi += n > 1
    print(f'{len(maps)} ranked maps on BeatLeader, {len(labels)} labeled ({multi} with several categories, highest kept), '
          + (f'{auto} auto tagged skipped' if not a.include_auto else 'auto tags included'))
    if a.labels_csv:
        with open(a.labels_csv, newline='', encoding='utf-8') as f:
            hand = {r['Id'].strip(): r['Label'].strip().lower() for r in csv.DictReader(f)}
        unknown = set(hand.values()) - set(bits)
        if unknown:
            sys.exit(f'unknown labels in {a.labels_csv}: {unknown} (known: {", ".join(bits)})')
        labels.update(hand)
        print(f'{len(hand)} labels from {a.labels_csv}')

    if a.features_db:
        c = sqlite3.connect(f'file:{a.features_db}?mode=ro', uri=True)
        ratings = {r[0]: r[1:] for r in c.execute('select Id, AccRating, TechRating, PassRating, Name, DifficultyName '
                                                  'from Leaderboards where AccRating > 0 or PassRating > 0')}
        c.close()
    else:
        ratings = {i: (m['AccRating'], m['TechRating'], m['PassRating'], m['Name'], '') for i, m in maps.items()
                   if m['AccRating'] > 0 or m['PassRating'] > 0}
    missing = [i for i in labels if i not in ratings]
    if missing:
        print(f'{len(missing)} labeled maps without ratings skipped')
    ids = sorted(i for i in labels if i in ratings)
    x = np.array([ratings[i][:3] for i in ids], dtype=np.float32)
    y = np.array([labels[i] for i in ids])
    classes = [c for c in bits if c in set(y)]
    counts = {c: int(np.sum(y == c)) for c in classes}
    print(f'training maps: {counts}')
    small = [c for c, n in counts.items() if n < 7]
    if small:
        sys.exit(f'too few maps labeled {small} for a stratified 70/15/15 split (7 at least, many more to learn it)')

    x_temp, x_test, y_temp, y_test = train_test_split(x, y, test_size=0.15, stratify=y, random_state=42)
    x_train, x_val, y_train, y_val = train_test_split(x_temp, y_temp, test_size=0.15 / 0.85, stratify=y_temp, random_state=42)

    best, best_name, best_acc = None, None, -1
    for name, model in candidates(a.family):
        model.fit(x_train, y_train)
        acc = balanced_accuracy_score(y_val, model.predict(x_val))
        if acc > best_acc:
            best, best_name, best_acc = model, name, acc
    test_acc = balanced_accuracy_score(y_test, best.predict(x_test))
    print(f'\nbest: {best_name}, val balanced accuracy {best_acc:.3f}, test {test_acc:.3f}')
    old_test = onnx_predict(a.old_model, x_test)
    print(f'current model on the same test maps: {balanced_accuracy_score(y_test, old_test):.3f}'
          + (' (it has no extreme)' if 'extreme' in classes else ''))
    print_confusion('Test maps', 'label', y_test, best.predict(x_test), classes)

    # Same interface as the original: input "float_input" [N, 3], first output the string label (zipmap off so the second
    # output is a plain probabilities tensor)
    onx = convert_sklearn(best, initial_types=[('float_input', FloatTensorType([None, 3]))], options={id(best): {'zipmap': False}})
    with open(a.out, 'wb') as f:
        f.write(onx.SerializeToString())
    print(f'\nwrote {a.out} ({os.path.getsize(a.out) // 1024} KB)')

    # Every rated map: the export against scikit-learn, then what the tags refresh would change
    all_ids = sorted(ratings)
    x_all = np.array([ratings[i][:3] for i in all_ids], dtype=np.float32)
    new = onnx_predict(a.out, x_all)
    mismatch = int(np.sum(new != best.predict(x_all)))
    print(f'ONNX vs scikit-learn on {len(all_ids)} maps: {mismatch} differ' + (' (float32 rounding near a boundary)' if mismatch else ''))
    if mismatch > len(all_ids) * 0.005:
        sys.exit('ONNX export disagrees with scikit-learn')
    old = onnx_predict(a.old_model, x_all)
    all_classes = list(bits)
    print(f'current model vs new model on every rated map: {np.mean(old == new) * 100:.1f}% agree')
    for name, v in (('current', old), ('new', new)):
        print(f'{name:>8}: ' + ', '.join(f'{c} {int(np.sum(v == c))}' for c in all_classes))
    print_confusion('Every rated map', 'current model', old, new, all_classes)
    changed = [k for k in range(len(all_ids)) if old[k] != new[k]]
    if changed and a.changed:
        print(f'\nFirst {min(a.changed, len(changed))} of {len(changed)} maps tagged differently (acc / tech / pass):')
        for k in changed[:a.changed]:
            r = ratings[all_ids[k]]
            print(f'  {old[k]:>8} -> {new[k]:<8} {r[0]:5.2f} {r[1]:5.2f} {r[2]:5.2f}  {r[3]} {r[4]}')


if __name__ == '__main__':
    main()
