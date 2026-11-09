using System;
using System.Collections.Generic;
using System.Linq;
using Arcade.Compose.MarkingMenu;
using Arcade.Compose.Operation;

namespace Arcade.Compose
{
    public enum ContextMenuState { Create, Normal, NormalSelected, CreateSelected }

    [Serializable]
    public sealed class ContextMenuStateOptions
    {
        public ContextMenuState State;
        public List<string> Hidden = new List<string>();
        public List<string> CircularOrder = new List<string>();
        public List<string> VerticalOrder = new List<string>();
    }

    [Serializable]
    public sealed class ContextMenuCustomization
    {
        public List<ContextMenuStateOptions> States = new List<ContextMenuStateOptions>();

        public ContextMenuStateOptions For(ContextMenuState state)
        {
            if (States == null) States = new List<ContextMenuStateOptions>();
            var options = States.FirstOrDefault(s => s != null && s.State == state);
            if (options == null)
            {
                options = new ContextMenuStateOptions { State = state };
                States.Add(options);
            }
            return options;
        }
    }

    public partial class AdeMarkingMenuManager
    {
        public sealed class MenuChoice
        {
            public readonly string Id, Title;
            public readonly MarkingMenuItem Item;
            public MenuChoice(string id, string title, MarkingMenuItem item)
            { Id = id; Title = title; Item = item; }
        }

        private static readonly string[] VerticalDefaultOrder =
        {
            "copy", "cut", "horizontal-cut", "mirror-x", "mirror-y", "snap-time", "convert-slide",
            "create-tap", "create-hold", "create-arc", "create-arctap", "create-slide",
            "create-none", "create-exit", "create-toggle", "grid", "delete"
        };
        private static readonly string[] CircularDefaultOrder =
        {
            // AA's supported creation entries, read clockwise from its left-hand origin.
            "create-none", "grid", "create-single-arctap", "create-arctap", "create-exit",
            "create-arc", "create-hold", "create-tap", "create-slide",
            "create-toggle", "delete", "snap-time", "mirror-x", "mirror-y", "copy", "cut", "horizontal-cut", "convert-slide"
        };

        // AA 1.13.1 normal selection: retain the common clockwise order, then AE additions.
        internal static readonly string[] NormalSelectedDefaultOrder =
        {
            "grid", "create-toggle", "mirror-y", "horizontal-cut", "convert-slide",
            "mirror-x", "cut", "copy", "snap-time", "delete"
        };

        private static readonly string[] NormalSelectedExtraIds = { "mirror-y", "horizontal-cut", "convert-slide" };

        public ContextMenuCustomization Customization
        {
            get
            {
                var preference = ArcadeComposeManager.Instance.ArcadePreference;
                return preference.ContextMenuItems ?? (preference.ContextMenuItems = new ContextMenuCustomization());
            }
        }

        public ContextMenuState CurrentState
        {
            get
            {
                bool selected = AdeSelectionManager.Instance.SelectedNotes.Count > 0;
                return AdeClickToCreate.Instance.Enable
                    ? (selected ? ContextMenuState.CreateSelected : ContextMenuState.Create)
                    : (selected ? ContextMenuState.NormalSelected : ContextMenuState.Normal);
            }
        }

        // Catalog all possible entries for this state, including entries currently unavailable
        // for the selected note type. Runtime availability still comes from the providers.
        public List<MenuChoice> GetMenuChoices(ContextMenuState state, bool vertical)
        {
            var choices = new List<MenuChoice>();
            var create = AdeClickToCreate.Instance;
            bool selected = state == ContextMenuState.NormalSelected || state == ContextMenuState.CreateSelected;
            if (state == ContextMenuState.Create || state == ContextMenuState.CreateSelected)
            {
                string[] ids = { "create-none", "create-tap", "create-hold", "create-arc", "create-arctap", "create-exit", "grid", "create-slide" };
                string[] titles = { "不创建", "Tap", "Hold", "Arc", "ArcTap", "退出点立得", "网格线", "Slide" };
                for (int i = 0; i < ids.Length && i < create.ClickToCreateItems.Length; i++)
                {
                    if (i == 7 && !vertical) continue;
                    if (i == 4 && !vertical)
                    {
                        var ring = ArrangeRingItems(new[] { create.ClickToCreateItems[i] });
                        choices.Add(new MenuChoice("create-single-arctap", "单Arctap", ring[0]));
                        choices.Add(new MenuChoice(ids[i], "Arctap", ring[1]));
                        continue;
                    }
                    choices.Add(new MenuChoice(ids[i], titles[i], create.ClickToCreateItems[i]));
                }
                if (selected)
                {
                    choices.Add(new MenuChoice("delete", "删除", create.Delete));
                    var mirror = AdeMirrorOperation.Instance;
                    if (mirror && mirror.ConvertToSlideItem)
                        choices.Add(new MenuChoice("convert-slide", "转Slide", mirror.ConvertToSlideItem));
                }
            }
            else
            {
                // Keep the serialized provider order used before the layout fix (087d922).
                foreach (var provider in Providers)
                {
                    if (selected && provider is AdeCopyOrCutPasteOperation copy)
                    {
                        choices.Add(new MenuChoice("copy", "复制", copy.CopyItem));
                        choices.Add(new MenuChoice("cut", "剪切", copy.CutItem));
                        choices.Add(new MenuChoice("horizontal-cut", "水平剪切", copy.HorizontalCutItem));
                    }
                    else if (provider is AdeGridManager grid)
                        choices.Add(new MenuChoice("grid", "网格线", grid.Entry[0]));
                    else if (selected && provider is AdeDeleteNoteOperation delete)
                        choices.Add(new MenuChoice("delete", "删除", delete.DeleteItem));
                    else if (selected && provider is AdeTimingSnapOperation snap)
                        choices.Add(new MenuChoice("snap-time", "时间对齐", snap.Entry));
                    else if (selected && provider is AdeMirrorOperation mirror)
                    {
                        choices.Add(new MenuChoice("mirror-x", "水平镜像", mirror.Entry));
                        choices.Add(new MenuChoice("mirror-y", "上下翻转", mirror.VerticalItem));
                        choices.Add(new MenuChoice("convert-slide", "转Slide", mirror.ConvertToSlideItem));
                    }
                    else if (provider is AdeClickToCreate)
                        choices.Add(new MenuChoice("create-toggle", "点立得", create.Entry[0]));
                }
            }
            if (state == ContextMenuState.NormalSelected)
            {
                foreach (var choice in choices.Where(c => c.Id == "mirror-x"))
                {
                    choice.Item.StartupText = "镜像";
                    choice.Item.Text = "镜像";
                }
                for (int i = 0; i < choices.Count; i++)
                {
                    var choice = choices[i];
                    if (choice.Id == "mirror-x") choices[i] = new MenuChoice(choice.Id, "镜像", choice.Item);
                }
            }
            foreach (var choice in choices)
                if (choice.Item) choice.Item.SetIcon(choice.Id);
            var options = Customization.For(state);
            var order = vertical ? options.VerticalOrder : options.CircularOrder;
            if (order == null || order.Count == 0)
                order = new List<string>(state == ContextMenuState.NormalSelected ? NormalSelectedDefaultOrder
                    : vertical ? VerticalDefaultOrder : CircularDefaultOrder);
            if (!vertical && order.Contains("create-arctap") && !order.Contains("create-single-arctap"))
            {
                order = new List<string>(order);
                order.Insert(order.IndexOf("create-arctap"), "create-single-arctap");
            }
            // Expand the former parent at its configured position without losing child ordering.
            if (state == ContextMenuState.NormalSelected && order.Contains("selection-more"))
            {
                var extras = order.Where(NormalSelectedExtraIds.Contains).Concat(NormalSelectedExtraIds).Distinct().ToArray();
                var expanded = order.Where(id => !NormalSelectedExtraIds.Contains(id)).ToList();
                int index = expanded.IndexOf("selection-more");
                expanded.RemoveAt(index); expanded.InsertRange(index, extras);
                order = expanded;
            }
            // Unknown/stale IDs are ignored; new entries retain their default relative order.
            return choices.OrderBy(c =>
            {
                int index = order.IndexOf(c.Id);
                return index < 0 ? int.MaxValue : index;
            }).ToList();
        }

        public MarkingMenuItem[] GetCurrentMenuItems()
        {
            var available = new List<MarkingMenuItem>();
            foreach (var provider in Providers)
            {
                var items = provider.MarkingMenuItems;
                if (items == null) continue;
                if (provider.IsOnlyMarkingMenu)
                {
                    // An ongoing operation's cancel menu must remain available.
                    if (!(provider is AdeClickToCreate)) return items;
                    available.Clear();
                    available.AddRange(items);
                    break;
                }
                available.AddRange(items);
            }
            bool vertical = ArcadeComposeManager.Instance.ArcadePreference.VerticalContextMenu;
            if (!vertical) available = new List<MarkingMenuItem>(ArrangeRingItems(available.ToArray()));
            var choices = GetMenuChoices(CurrentState, vertical);
            var hidden = Customization.For(CurrentState).Hidden;
            var result = choices.Where(c => available.Contains(c.Item) && (hidden == null || !hidden.Contains(c.Id)))
                .Select(c => c.Item).ToList();
            result.AddRange(available.Where(item => !choices.Any(c => c.Item == item)));
            return result.ToArray();
        }
    }
}
