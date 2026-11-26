using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ArtTools.EnvironmentArt
{
    [FilePath(FolderColorState.SavedAt, FilePathAttribute.Location.ProjectFolder)]
    internal class FolderColorState : ScriptableSingleton<FolderColorState>
    {
        internal const string SavedAt = "UserSettings/ArtToolsFolderColors.asset";

        internal const string Inherit = "";
        internal const string NoColor = "-";

        [Serializable]
        internal class Entry
        {
            public string guid;
            public string color = Inherit;
            public string badge = "";
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        [SerializeField] private List<string> _palette = new List<string>();

        [SerializeField] private int _revision;
        [NonSerialized] private int _seenRevision;

        private Dictionary<string, Entry> _byGuid;
        private bool _unsaved;

        private void OnEnable() => _seenRevision = _revision;

        internal static IReadOnlyList<string> Palette => instance._palette;

        internal static void AddToPalette(string color)
        {
            if (instance._palette.Contains(color)) return;
            instance._palette.Add(color);
            instance.Save(true);
        }

        internal static void RemoveFromPalette(string color)
        {
            if (instance._palette.Remove(color)) instance.Save(true);
        }

        internal static void Flush()
        {
            if (!instance._unsaved) return;
            instance._unsaved = false;
            instance.Save(true);
        }

        internal static event Action Changed;

        internal static Entry Find(string guid)
        {
            Map().TryGetValue(guid, out var entry);
            return entry;
        }

        internal static void Edit(IEnumerable<string> guids, Action<Entry> change, bool save = true)
        {
            var state = instance;

            Undo.RecordObject(state, "Folder Color");
            state._revision++;
            state._seenRevision = state._revision;

            var map = Map();
            foreach (var guid in guids)
            {
                if (!map.TryGetValue(guid, out var entry))
                {
                    entry = new Entry { guid = guid };
                    state._entries.Add(entry);
                    map[guid] = entry;
                }
                change(entry);
            }

            state._entries.RemoveAll(e =>
                (e.color == Inherit && e.badge == "") || string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(e.guid)));
            state._byGuid = null;
            state._unsaved = !save;
            if (save) state.Save(true);

            Changed?.Invoke();
        }

        internal static void AfterUndoRedo()
        {
            var state = instance;
            if (state._revision == state._seenRevision) return;

            state._seenRevision = state._revision;
            state._byGuid = null;
            state._unsaved = false;
            state.Save(true);
            Changed?.Invoke();
        }

        internal static List<string> ColoredInside(string[] guids)
        {
            var roots = new List<string>(guids.Length);
            foreach (var guid in guids) roots.Add(AssetDatabase.GUIDToAssetPath(guid) + "/");

            var inside = new List<string>();
            foreach (var entry in instance._entries)
            {
                if (entry.color == Inherit || Array.IndexOf(guids, entry.guid) >= 0) continue;

                var path = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (roots.Exists(root => path.StartsWith(root, StringComparison.Ordinal))) inside.Add(entry.guid);
            }
            return inside;
        }

        private static Dictionary<string, Entry> Map()
        {
            var state = instance;
            if (state._byGuid != null) return state._byGuid;

            state._byGuid = new Dictionary<string, Entry>(state._entries.Count);
            foreach (var entry in state._entries) state._byGuid[entry.guid] = entry;
            return state._byGuid;
        }
    }
}
