using System.Reflection;

namespace RatingAPI.Pp
{
    // Score multiplier of each modifier (PpCalculator.ScorePp uses RankedMap)
    public class ModifiersMap
    {
        public int ModifierId { get; set; }

        public float DA { get; set; } = 0.0f;
        public float FS { get; set; } = 0.0f;
        public float SF { get; set; } = 0.0f;
        public float SS { get; set; } = 0.0f;
        public float GN { get; set; } = 0.0f;
        public float NA { get; set; } = -0.3f;
        public float NB { get; set; } = -0.2f;
        public float NF { get; set; } = -0.5f;
        public float NO { get; set; } = -0.2f;
        public float PM { get; set; } = 0.0f;
        public float SC { get; set; } = 0.0f;
        public float SA { get; set; } = 0.0f;
        public float OP { get; set; } = -0.5f;

        public static ModifiersMap RankedMap() {
            return new ModifiersMap {
                DA = 0.0f,
                FS = 0.0f,
                SF = 0.0f,
                SS = 0.0f,
                GN = 0.0f,
                NA = -0.3f,
                NB = -0.2f,
                NF = -1.0f,
                NO = -0.2f,
                PM = 0.0f,
                SC = 0.0f,
                SA = 0.0f,
                OP = -0.5f,
            };
        }

        public bool EqualTo(ModifiersMap? other) {
            return other != null && DA == other.DA && FS == other.FS && SS == other.SS && SF == other.SF && GN == other.GN && NA == other.NA && NB == other.NB && NF == other.NF && NO == other.NO && PM == other.PM && SC == other.SC && SA == other.SA && OP == other.OP;
        }
    }

    public static class ModifiersMapExtensions
    {
        // The modifiers (DA, FS, ...), in declaration order
        private static readonly PropertyInfo[] aModifiers = typeof(ModifiersMap).GetProperties().Where(p => p.PropertyType == typeof(float) && p.Name.Length == 2).ToArray();

        public static float GetTotalMultiplier(this ModifiersMap modifiersObject, string modifiers, bool speedModifiers)
        {
            float multiplier = 1;

            foreach (PropertyInfo modifierProp in aModifiers)
            {
                if (!speedModifiers && modifierProp.Name is "SF" or "SS" or "FS")
                    continue;

                if (modifiers.Contains(modifierProp.Name, StringComparison.Ordinal))
                {
                    multiplier += (float)modifierProp.GetValue(modifiersObject)!;
                }
            }

            return multiplier;
        }

        public static float GetPositiveMultiplier(this ModifiersMap modifiersObject, string modifiers)
        {
            float multiplier = 1;

            foreach (PropertyInfo modifierProp in aModifiers)
            {
                float value = (float)modifierProp.GetValue(modifiersObject)!;
                if (modifiers.Contains(modifierProp.Name) && value > 0) { multiplier += value; }
            }

            return multiplier;
        }

        public static float GetNegativeMultiplier(this ModifiersMap modifiersObject, string modifiers, bool ignoreNF = false)
        {
            float multiplier = 1;

            foreach (PropertyInfo modifierProp in aModifiers)
            {
                if (ignoreNF && modifierProp.Name == "NF") continue;
                float value = (float)modifierProp.GetValue(modifiersObject)!;
                if (modifiers.Contains(modifierProp.Name) && value < 0) { multiplier += value; }
            }

            return multiplier;
        }

        public static (string, float) GetNegativeMultipliers(this ModifiersMap modifiersObject, string modifiers)
        {
            float multiplier = 1;
            List<string> modifierArray = new List<string>();

            foreach (PropertyInfo modifierProp in aModifiers)
            {
                float value = (float)modifierProp.GetValue(modifiersObject)!;
                if (modifiers.Contains(modifierProp.Name) && value < 0) {
                    multiplier += value;
                    modifierArray.Add(modifierProp.Name);
                }
            }

            return (string.Join(",", modifierArray), multiplier);
        }
    }
}
