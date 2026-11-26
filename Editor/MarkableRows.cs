using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static ArtTools.EnvironmentArt.ArtToolsStyles;
using Object = UnityEngine.Object;

namespace ArtTools.EnvironmentArt
{
    internal class MarkableRow
    {
        public GameObject Object;
        public List<GameObject> Members;
        public string Label;
        public VisualElement Row;
        public Toggle Check;
        public bool Locked;

        public bool Marked => Check.value && !Locked;

        public IEnumerable<Object> Selectable()
        {
            if (Members == null)
            {
                if (Object != null) yield return Object;
                yield break;
            }

            foreach (var member in Members)
                if (member != null) yield return member;
        }
    }

    internal class MarkableRows<TRow> where TRow : MarkableRow
    {
        public readonly List<TRow> Rows = new List<TRow>();

        public event Action Changed;

        private int _checkAnchor = -1;
        private int _nameAnchor = -1;

        public void Clear()
        {
            Rows.Clear();
            _checkAnchor = -1;
            _nameAnchor = -1;
        }

        public Dictionary<GameObject, TRow> ByObject()
        {
            return Rows.Where(r => r.Object != null).ToDictionary(r => r.Object);
        }

        public VisualElement Add(TRow entry, bool marked, Color nameColor)
        {
            var row = ArtToolsStyles.Row();
            row.style.height = 22f;
            Radius(row, 3f);
            Padding(row, 0f, 2f);

            var check = new Toggle { value = marked && !entry.Locked };
            check.style.marginRight = 4f;
            check.SetEnabled(!entry.Locked);
            check.RegisterCallback<PointerDownEvent>(e =>
            {
                if (entry.Locked || e.button != 0 || !e.shiftKey) return;
                check.SetValueWithoutNotify(!check.value);
                OnCheck(entry, check.value, true);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            check.RegisterValueChangedCallback(e => OnCheck(entry, e.newValue, false));
            row.Add(check);

            var name = new Label(entry.Label ?? entry.Object.name);
            name.style.flexGrow = 1f;
            name.style.flexShrink = 1f;
            name.style.flexBasis = 0f;
            name.style.overflow = Overflow.Hidden;
            name.style.color = nameColor;
            name.RegisterCallback<ClickEvent>(e => OnNameClick(entry, e));
            row.Add(name);

            entry.Row = row;
            entry.Check = check;
            Rows.Add(entry);
            return row;
        }

        public VisualElement MarkLinks()
        {
            var links = ArtToolsStyles.Row();
            links.Add(LinkButton("Mark all", () => MarkAll(true)));
            links.Add(LinkButton("Mark none", () => MarkAll(false)));
            return links;
        }

        public void MarkAll(bool value)
        {
            foreach (var row in Rows.Where(r => !r.Locked)) row.Check.SetValueWithoutNotify(value);
            if (!value) Deselect(Rows);
            Changed?.Invoke();
        }

        public List<TRow> SelectedWith(TRow row)
        {
            return IsSelected(row)
                ? Rows.Where(r => r != row && !r.Locked && IsSelected(r)).ToList()
                : new List<TRow>();
        }

        public void RefreshSelection()
        {
            var tint = new Color(Accent.r, Accent.g, Accent.b, 0.3f);
            foreach (var row in Rows)
                row.Row.style.backgroundColor = IsSelected(row) ? tint : Color.clear;
        }

        public void Notify() => Changed?.Invoke();

        private static bool IsSelected(TRow row) => row.Object != null && Selection.Contains(row.Object);

        private static void Deselect(IEnumerable<TRow> rows)
        {
            var leaving = new HashSet<Object>(rows.SelectMany(r => r.Selectable()));
            var staying = Selection.objects.Where(o => !leaving.Contains(o)).ToArray();
            if (staying.Length != Selection.objects.Length) Selection.objects = staying;
        }

        private Object[] ObjectsBetween(int from, int to)
        {
            return Rows.Skip(from).Take(to - from + 1)
                .SelectMany(r => r.Selectable())
                .ToArray();
        }

        private void OnNameClick(TRow row, ClickEvent e)
        {
            if (row.Object == null || e.button != 0) return;

            int index = Rows.IndexOf(row);
            if (e.shiftKey && _nameAnchor >= 0 && _nameAnchor < Rows.Count)
            {
                Selection.objects = ObjectsBetween(Mathf.Min(_nameAnchor, index), Mathf.Max(_nameAnchor, index));
                return;
            }

            if (e.actionKey)
            {
                var clicked = row.Selectable().ToList();
                var selected = Selection.objects.ToList();
                if (IsSelected(row)) selected.RemoveAll(clicked.Contains);
                else selected.AddRange(clicked.Where(o => !selected.Contains(o)));
                Selection.objects = selected.ToArray();
            }
            else
            {
                Selection.objects = row.Selectable().ToArray();
                EditorGUIUtility.PingObject(row.Object);
            }
            _nameAnchor = index;
        }

        private void OnCheck(TRow row, bool value, bool shift)
        {
            int index = Rows.IndexOf(row);

            if (shift && _checkAnchor >= 0 && _checkAnchor < Rows.Count)
            {
                int from = Mathf.Min(_checkAnchor, index);
                int to = Mathf.Max(_checkAnchor, index);
                for (int i = from; i <= to; i++)
                    if (!Rows[i].Locked) Rows[i].Check.SetValueWithoutNotify(value);

                if (value) Selection.objects = ObjectsBetween(from, to);
                else Deselect(Rows.Skip(from).Take(to - from + 1));
            }
            else
            {
                var others = SelectedWith(row);
                foreach (var other in others) other.Check.SetValueWithoutNotify(value);

                if (!value && others.Count > 0)
                {
                    others.Add(row);
                    Deselect(others);
                }
            }

            _checkAnchor = index;
            Changed?.Invoke();
        }
    }
}
