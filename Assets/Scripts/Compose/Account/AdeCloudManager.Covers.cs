using System.Threading;
using Arcade.Util.UniTaskHelper;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Arcade.Compose
{
    public sealed partial class AdeCloudManager
    {
        private readonly Queue<(CloudChart chart, RawImage image)> coverQueue = new Queue<(CloudChart, RawImage)>();
        private readonly Dictionary<int, (string version, Texture2D texture)> coverCache = new Dictionary<int, (string, Texture2D)>();
        private bool coverLoading;
        private CancellationTokenSource coverCancellation;

        private void CreateListCover(Transform parent, CloudChart chart)
        {
            var frame = new GameObject("Cover", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(LayoutElement));
            frame.transform.SetParent(parent, false);
            var size = frame.GetComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = size.minHeight = size.preferredHeight = 128;
            var background = frame.GetComponent<Image>();
            background.sprite = AdeUiTheme.RoundedSurface;
            background.type = Image.Type.Sliced;
            background.color = AdeUiTheme.Current.Input;
            background.raycastTarget = false;
            difficultySurfaces.Add(background);
            var fallback = ListText(frame.transform, "CoverPlaceholder", "暂无封面", 20, 128, true);
            fallback.alignment = TMPro.TextAlignmentOptions.Center;
            fallback.rectTransform.anchorMin = Vector2.zero;
            fallback.rectTransform.anchorMax = Vector2.one;
            fallback.rectTransform.offsetMin = fallback.rectTransform.offsetMax = Vector2.zero;
            var imageObject = new GameObject("Artwork", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(frame.transform, false);
            var image = imageObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.clear;
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            coverQueue.Enqueue((chart, image));
        }

        private async UniTask LoadListCovers()
        {
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            coverCancellation = cancellation;
            try
            {
                while (coverQueue.Count > 0)
                {
                    var item = coverQueue.Dequeue();
                    if (!item.image) continue;
                    if (!coverCache.TryGetValue(item.chart.ArchiveId, out var cached) || cached.version != item.chart.UpdatedAt)
                    {
                        using (var request = Get($"/v2/game/editor/charts/{item.chart.ArchiveId}/files/base.jpg"))
                        {
                            request.downloadHandler.Dispose();
                            request.downloadHandler = new DownloadHandlerTexture(true);
                            request.timeout = 20;
                            await request.SendWithResultAsync(cancellation.Token);
                            if (request.result != UnityWebRequest.Result.Success) continue;
                            var texture = DownloadHandlerTexture.GetContent(request);
                            if (!texture) continue;
                            if (cached.texture) Destroy(cached.texture);
                            cached = (item.chart.UpdatedAt, texture);
                            coverCache[item.chart.ArchiveId] = cached;
                        }
                    }
                    if (!item.image) continue;
                    item.image.texture = cached.texture;
                    float aspect = (float)cached.texture.width / cached.texture.height;
                    item.image.uvRect = aspect > 1
                        ? new Rect((1 - 1 / aspect) / 2, 0, 1 / aspect, 1)
                        : new Rect(0, (1 - aspect) / 2, 1, aspect);
                    item.image.color = Color.white;
                }
            }
            finally
            {
                if (coverCancellation == cancellation)
                {
                    coverCancellation = null;
                    coverLoading = false;
                }
                cancellation.Dispose();
            }
        }

        private void ReleaseListCovers()
        {
            coverCancellation?.Cancel();
            coverCancellation = null;
            coverLoading = false;
            foreach (var entry in coverCache.Values)
                if (entry.texture) Destroy(entry.texture);
            coverCache.Clear();
            coverQueue.Clear();
        }
    }
}
