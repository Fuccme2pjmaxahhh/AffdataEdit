using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeSettingsDialog
    {
        private readonly List<Image> storageCards = new List<Image>();
        private readonly List<TMP_Text> storageLabels = new List<TMP_Text>();
        private readonly List<TMP_Text> storagePaths = new List<TMP_Text>();
        private readonly List<Image> storageIcons = new List<Image>();
        private readonly List<Button> storageButtons = new List<Button>();
        // Moved after the borrowed audio controls have been mounted.
        private Transform slowAudioCacheEntry;

        private void BuildStorage()
        {
            AddStorageLocation("编辑器设置", AdeStorage.SettingsPath, true);
            AddStorageLocation("云端谱面", AdeStorage.CloudChartsFolder, false);
            AddStorageLocation("账号登录状态", AdeStorage.AccountPath, true);
            slowAudioCacheEntry = AddStorageLocation("慢速播放缓存 · 上限 1 GB", Arcade.Audio.SlowAudioCache.CacheFolder, false, Page.Audio);
        }

        private RectTransform AddStorageLocation(string title, string path, bool file, Page page = Page.Storage)
        {
            var card = Rect(title, pages[(int)page], typeof(Image), typeof(LayoutElement));
            card.GetComponent<LayoutElement>().preferredHeight = 148;
            storageCards.Add(card.GetComponent<Image>());
            var heading = Rect("Title", card, typeof(TextMeshProUGUI));
            heading.anchorMin = new Vector2(0, 1); heading.anchorMax = Vector2.one;
            heading.offsetMin = new Vector2(16, -44); heading.offsetMax = new Vector2(-16, -12);
            var label = heading.GetComponent<TMP_Text>();
            AdeEditorText.Configure(label, AdeEditorText.BodySize);
            label.text = title;
            label.overflowMode = TextOverflowModes.Ellipsis;
            storageLabels.Add(label);

            var location = Rect("Path", card, typeof(TextMeshProUGUI));
            location.anchorMin = new Vector2(0, 1); location.anchorMax = Vector2.one;
            location.offsetMin = new Vector2(16, -80); location.offsetMax = new Vector2(-56, -48);
            var pathText = location.GetComponent<TMP_Text>();
            AdeEditorText.Configure(pathText, AdeEditorText.CaptionSize);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            pathText.text = !string.IsNullOrEmpty(home) && path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? "~" + path.Substring(home.Length) : path;
            pathText.overflowMode = TextOverflowModes.Ellipsis;
            storagePaths.Add(pathText);
            var copy = StorageButton(card, "", Resources.Load<Sprite>("AffdataEdit/Icons/Copy"), () =>
            {
                GUIUtility.systemCopyBuffer = path;
                AdeToast.Instance?.Show("已复制完整路径");
            });
            var copyRect = (RectTransform)copy.transform;
            copyRect.anchorMin = copyRect.anchorMax = Vector2.one;
            copyRect.pivot = Vector2.one; copyRect.sizeDelta = new Vector2(32, 32);
            copyRect.anchoredPosition = new Vector2(-12, -48);
            var copyIcon = (RectTransform)copy.transform.Find("Icon");
            copyIcon.anchorMin = copyIcon.anchorMax = new Vector2(.5f, .5f);
            copyIcon.anchoredPosition = Vector2.zero;

            var actions = Rect("Actions", card, typeof(HorizontalLayoutGroup));
            actions.anchorMin = Vector2.zero; actions.anchorMax = new Vector2(1, 0);
            actions.offsetMin = new Vector2(16, 12); actions.offsetMax = new Vector2(-16, 52);
            var layout = actions.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10; layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            StorageButton(actions, "打开文件夹", (icons[(int)Page.Storage] as Image)?.sprite, () =>
            {
                try
                {
                    string folder = file ? Path.GetDirectoryName(path) : path;
                    Directory.CreateDirectory(folder);
                    Arcade.Util.Shell.FileBrowser.OpenExplorer(folder);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception)
                {
                    AdeToast.Instance?.Show("无法打开文件夹：" + error.Message);
                }
            });
            if (page == Page.Audio)
                StorageButton(actions, "清理缓存", Resources.Load<Sprite>("AffdataEdit/Icons/Trash"), () =>
                {
                    try
                    {
                        int deleted = Gameplay.ArcAudioManager.ClearSlowAudioCache();
                        AdeToast.Instance?.Show(deleted == 0 ? "缓存已是空的" : "已清理慢速播放缓存");
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                    {
                        AdeToast.Instance?.Show("无法清理缓存：" + error.Message);
                    }
                });
            return card;
        }

        private Button StorageButton(Transform parent, string title, Sprite sprite, UnityEngine.Events.UnityAction action)
        {
            var button = MakeButton(parent, title, action);
            var size = button.GetComponent<LayoutElement>();
            size.preferredWidth = title.Length * AdeEditorText.BodySize + 60; size.preferredHeight = 40;
            button.GetComponentInChildren<TMP_Text>().rectTransform.offsetMin = new Vector2(44, 0);
            var icon = Rect("Icon", button.transform, typeof(Image));
            icon.anchorMin = icon.anchorMax = new Vector2(0, .5f);
            icon.anchoredPosition = new Vector2(24, 0); icon.sizeDelta = new Vector2(22, 22);
            var image = icon.GetComponent<Image>();
            image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
            storageIcons.Add(image);
            storageButtons.Add(button);
            return button;
        }

        private void StyleStorage()
        {
            foreach (var card in storageCards)
            {
                card.sprite = AdeUiTheme.RoundedSurface;
                card.type = Image.Type.Sliced;
                card.color = AdeUiTheme.Current.Input;
            }
            foreach (var label in storageLabels) label.color = AdeUiTheme.Current.Text;
            foreach (var path in storagePaths) path.color = AdeUiTheme.Current.Muted;
            foreach (var icon in storageIcons) icon.color = AdeUiTheme.Current.Muted;
            foreach (var button in storageButtons)
            {
                button.image.sprite = AdeUiTheme.RoundedSurface;
                button.image.type = Image.Type.Sliced;
                button.image.color = AdeUiTheme.Current.Panel;
                button.GetComponentInChildren<TMP_Text>().color = AdeUiTheme.Current.Text;
            }
        }
    }
}
