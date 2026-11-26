using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArtTools.EnvironmentArt
{
    internal static class SiblingNames
    {
        internal enum DuplicateScheme
        {
            Parenthesis,
            Dot,
            Underscore,
        }

        internal static string WithoutDuplicateSuffix(string name, DuplicateScheme scheme)
        {
            var trimmed = name.Trim();
            string suffix = scheme == DuplicateScheme.Dot ? @"(?:\s*\(\d+\)|\.\d+)+$" : @"(?:\s*\(\d+\))+$";
            var plain = Regex.Replace(trimmed, suffix, "");
            return plain.Length == 0 ? trimmed : plain;
        }

        internal static string SeriesBase(string name, DuplicateScheme scheme)
        {
            var plain = WithoutDuplicateSuffix(name, scheme);
            string number = scheme == DuplicateScheme.Underscore ? @"_\d+$" : @"_\d{2,}$";
            var seriesBase = Regex.Replace(plain, number, "");
            return seriesBase.Length == 0 ? plain : seriesBase;
        }

        internal static bool TryGetNumber(string name, string seriesBase, out int number)
        {
            number = 0;

            int digits = name.Length - seriesBase.Length - 1;
            if (digits < 2 || digits > 9) return false;
            if (name[seriesBase.Length] != '_' || !name.StartsWith(seriesBase, System.StringComparison.Ordinal)) return false;

            for (int i = seriesBase.Length + 1; i < name.Length; i++)
            {
                char digit = name[i];
                if (digit < '0' || digit > '9') return false;
                number = number * 10 + (digit - '0');
            }

            return true;
        }

        internal static string[] Normalized(
            List<(string Name, string SeriesBase)> selected,
            IEnumerable<string> unselectedNames,
            DuplicateScheme scheme)
        {
            var taken = new HashSet<string>(unselectedNames);
            var kept = new HashSet<string>();
            var newNames = new string[selected.Count];
            var seriesSizes = selected.GroupBy(s => s.SeriesBase).ToDictionary(series => series.Key, series => series.Count());

            for (int i = 0; i < selected.Count; i++)
            {
                var (name, seriesBase) = selected[i];
                bool tidy = TryGetNumber(name, seriesBase, out _) || (name == seriesBase && seriesSizes[seriesBase] == 1);
                if (!tidy || !kept.Add(name)) continue;

                taken.Add(name);
                newNames[i] = name;
            }

            var lastNumbers = new Dictionary<string, int>();
            for (int i = 0; i < selected.Count; i++)
            {
                if (newNames[i] != null) continue;

                var (name, seriesBase) = selected[i];

                var plain = WithoutDuplicateSuffix(name, scheme);
                if (TryGetNumber(plain, seriesBase, out _) && taken.Add(plain))
                {
                    newNames[i] = plain;
                    continue;
                }

                if (!lastNumbers.TryGetValue(seriesBase, out int number)) number = HighestNumber(taken, seriesBase);

                bool numbered = seriesSizes[seriesBase] > 1 || number > 0;
                string newName = seriesBase;
                if (numbered || !taken.Add(newName))
                {
                    string format = seriesSizes[seriesBase] >= 100 ? "000" : "00";
                    do newName = $"{seriesBase}_{(++number).ToString(format)}";
                    while (!taken.Add(newName));
                }

                lastNumbers[seriesBase] = number;
                newNames[i] = newName;
            }

            return newNames;
        }

        private static int HighestNumber(HashSet<string> names, string seriesBase)
        {
            int highest = 0;
            foreach (var name in names)
                if (TryGetNumber(name, seriesBase, out int number) && number > highest) highest = number;
            return highest;
        }
    }
}
