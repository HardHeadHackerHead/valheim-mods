using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Terminal
{
    /// <summary>A laid-out page: everything to draw, where the links are, and a grid that finds what is in view quickly.</summary>
    internal sealed class Doc
    {
        internal const float Bucket = 300f;
        internal Layouter.Item[] Items;
        internal Layouter.Link[] Links;
        internal string[] PictureUrls;
        internal float Height;
        internal Color Page = Color.white;
        internal Dictionary<int, List<int>> Grid = new Dictionary<int, List<int>>();
        internal string Title = "";

        internal static Doc From(Layouter layout, string title)
        {
            var doc = new Doc { Items = layout.Items.ToArray(), Links = layout.Links.ToArray(), Height = layout.Height, Page = layout.Page, Title = title };
            var urls = new List<string>();
            foreach (KeyValuePair<string, Node> p in layout.Pictures) urls.Add(p.Key);
            doc.PictureUrls = urls.ToArray();
            for (int i = 0; i < doc.Items.Length; i++)
            {
                Layouter.Item it = doc.Items[i];
                int from = Mathf.Max(0, Mathf.FloorToInt(it.Y / Bucket)), to = Mathf.Max(from, Mathf.FloorToInt((it.Y + Mathf.Max(0f, it.H)) / Bucket));
                to = Mathf.Min(to, from + 200);   // (a very tall box is listed in the first buckets only: it is a background)
                for (int b = from; b <= to; b++)
                {
                    if (!doc.Grid.TryGetValue(b, out List<int> list)) doc.Grid[b] = list = new List<int>();
                    list.Add(i);
                }
            }
            return doc;
        }

        internal string LinkAt(float x, float y)
        {
            string found = null;
            foreach (Layouter.Link l in Links)
                if (x >= l.X && x <= l.X + l.W && y >= l.Y && y <= l.Y + l.H) found = l.Href;   // (the last one drawn is on top)
            return found;
        }
    }

    /// <summary>
    /// Draws a Doc into the browser window: only what is inside the window (found through the grid) has UI objects, taken from pools and
    /// kept while it stays in view, so scrolling just moves them. Boxes are images, text runs are TextMeshPro, pictures are raw images
    /// that show a grey box until their picture has loaded (ImageStore). Everything is in the desktop's 1280x720 space; the page's CSS
    /// pixels are drawn at Zoom.
    /// </summary>
    internal sealed class PageView
    {
        internal const float Zoom = 1.4f;
        private readonly RectTransform _area, _boxes, _pics, _texts;   // (three layers: boxes under pictures under text)
        private readonly Image _background, _thumb;
        private readonly float _w, _h;

        private readonly Dictionary<int, GameObject> _active = new Dictionary<int, GameObject>();
        private readonly Stack<GameObject> _boxPool = new Stack<GameObject>(), _textPool = new Stack<GameObject>(), _picPool = new Stack<GameObject>();
        private Doc _doc;
        private float _drawnScroll = float.NaN;
        private int _drawnLoaded = -1;

        internal PageView(Transform parent, float x, float y, float w, float h)
        {
            _w = w; _h = h;
            var go = new GameObject("PageView", typeof(RectTransform), typeof(RectMask2D)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            _area = (RectTransform)go.transform;
            _area.anchorMin = _area.anchorMax = _area.pivot = new Vector2(0f, 1f);
            _area.anchoredPosition = new Vector2(x, -y);
            _area.sizeDelta = new Vector2(w, h);
            _background = Desktop.Box("Background", _area, 0f, 0f, w, h, Color.white);
            _boxes = Layer("Boxes", w, h); _pics = Layer("Pictures", w, h); _texts = Layer("Text", w, h);
            _thumb = Desktop.Box("Thumb", _area, w - 12f, 0f, 10f, 40f, new Color(0.45f, 0.5f, 0.55f, 0.8f));
        }

        private RectTransform Layer(string name, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = _area.gameObject.layer };
            go.transform.SetParent(_area, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        internal float ViewportCss => (_w - 16f) / Zoom;
        internal float MaxScroll => _doc == null ? 0f : Mathf.Max(0f, _doc.Height - _h / Zoom);

        internal void Show(Doc doc)
        {
            if (ReferenceEquals(_doc, doc)) return;
            _doc = doc;
            Recycle();
            _drawnScroll = float.NaN;
            _background.color = doc != null ? doc.Page : Color.white;
        }

        private void Recycle()
        {
            foreach (KeyValuePair<int, GameObject> a in _active) Release(a.Key, a.Value);
            _active.Clear();
        }

        private void Release(int index, GameObject go)
        {
            go.SetActive(false);
            Kind k = KindOf(go);
            (k == Kind.Box ? _boxPool : k == Kind.Text ? _textPool : _picPool).Push(go);
        }

        private enum Kind { Box, Text, Pic }
        private static Kind KindOf(GameObject go) => go.GetComponent<TextMeshProUGUI>() != null ? Kind.Text : go.GetComponent<RawImage>() != null ? Kind.Pic : Kind.Box;

        /// <summary>Move what is drawn to match the scroll position (css pixels from the top), and pictures that have arrived.</summary>
        internal void Update(float scroll)
        {
            if (_doc == null) return;
            scroll = Mathf.Clamp(scroll, 0f, MaxScroll);
            int loaded = ImageStore.Version;
            bool same = Mathf.Approximately(scroll, _drawnScroll) && loaded == _drawnLoaded;
            if (same) return;
            bool pictures = loaded != _drawnLoaded;
            _drawnScroll = scroll; _drawnLoaded = loaded;

            float top = scroll - 80f, bottom = scroll + _h / Zoom + 80f;
            var wanted = new HashSet<int>();
            for (int b = Mathf.Max(0, Mathf.FloorToInt(top / Doc.Bucket)); b <= Mathf.FloorToInt(bottom / Doc.Bucket); b++)
            {
                if (!_doc.Grid.TryGetValue(b, out List<int> list)) continue;
                foreach (int index in list)
                {
                    Layouter.Item it = _doc.Items[index];
                    if (it.Y + it.H >= top && it.Y <= bottom) wanted.Add(index);
                }
            }
            var gone = new List<int>();
            foreach (KeyValuePair<int, GameObject> a in _active) if (!wanted.Contains(a.Key)) gone.Add(a.Key);
            foreach (int index in gone) { Release(index, _active[index]); _active.Remove(index); }
            var order = new List<int>(wanted);
            order.Sort();   // (in the page's own order, so a box made in this pass lies under what is drawn in it)
            foreach (int index in order)
            {
                Layouter.Item it = _doc.Items[index];
                if (!_active.TryGetValue(index, out GameObject go))
                {
                    go = Take(it);
                    _active[index] = go;
                    pictures |= it.Kind == 3;
                }
                var rt = (RectTransform)go.transform;
                rt.anchoredPosition = new Vector2(it.X * Zoom, -(it.Y - scroll) * Zoom);
                if (it.Kind == 3 && pictures) SetPicture(go, it);
            }
            // the scroll bar
            float docH = Mathf.Max(_doc.Height, 1f), viewH = _h / Zoom;
            bool bar = docH > viewH;
            _thumb.gameObject.SetActive(bar);
            if (bar)
            {
                float th = Mathf.Max(30f, _h * viewH / docH);
                var rt = _thumb.rectTransform;
                rt.sizeDelta = new Vector2(10f, th);
                rt.anchoredPosition = new Vector2(_w - 12f, -(_h - th) * (scroll / Mathf.Max(1f, docH - viewH)));
                rt.SetAsLastSibling();   // (above the layers)
            }
        }

        private GameObject Take(Layouter.Item it)
        {
            GameObject go;
            if (it.Kind == 2)
            {
                go = _textPool.Count > 0 ? _textPool.Pop() : NewText();
                var t = go.GetComponent<TextMeshProUGUI>();
                t.fontSize = it.Size * Zoom;
                t.color = it.C;
                t.fontStyle = (it.Bold ? FontStyles.Bold : FontStyles.Normal) | (it.Italic ? FontStyles.Italic : FontStyles.Normal);
                t.text = it.Text;
                ((RectTransform)go.transform).sizeDelta = new Vector2((it.W + 6f) * Zoom, (it.H + 2f) * Zoom);
            }
            else if (it.Kind == 3)
            {
                go = _picPool.Count > 0 ? _picPool.Pop() : NewPic();
                ((RectTransform)go.transform).sizeDelta = new Vector2(it.W * Zoom, it.H * Zoom);
            }
            else
            {
                go = _boxPool.Count > 0 ? _boxPool.Pop() : NewBox();
                go.GetComponent<Image>().color = it.C;
                ((RectTransform)go.transform).sizeDelta = new Vector2(it.W * Zoom, Mathf.Max(1f, it.H * Zoom));
            }
            go.SetActive(true);
            return go;
        }

        private static void SetPicture(GameObject go, Layouter.Item it)
        {
            var raw = go.GetComponent<RawImage>();
            Texture2D tex = it.Url != null ? ImageStore.Get(it.Url) : null;
            raw.texture = tex;
            raw.color = tex != null ? Color.white : it.C;
        }

        private GameObject NewBox()
        {
            var go = new GameObject("Box", typeof(RectTransform), typeof(Image)) { layer = _area.gameObject.layer };
            go.transform.SetParent(_boxes, false);
            Pin((RectTransform)go.transform);
            go.GetComponent<Image>().raycastTarget = false;
            return go;
        }

        private GameObject NewPic()
        {
            var go = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)) { layer = _area.gameObject.layer };
            go.transform.SetParent(_pics, false);
            Pin((RectTransform)go.transform);
            go.GetComponent<RawImage>().raycastTarget = false;
            return go;
        }

        private GameObject NewText()
        {
            TextMeshProUGUI t = Desktop.Label("Text", _texts, "", 20f, Color.black, TextAlignmentOptions.TopLeft, 0f, 0f, 100f, 30f);
            t.richText = false;
            Pin((RectTransform)t.transform);
            return t.gameObject;
        }

        private static void Pin(RectTransform rt) { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f); }
    }

    /// <summary>
    /// The pictures of web pages: fetched over https (each redirect checked like a page's), kept as textures (the last forty), and told to the
    /// layout by size so a page lays out once its pictures are known. PNG and JPEG only (what Unity reads); anything else stays a grey box.
    /// </summary>
    internal static class ImageStore
    {
        private sealed class Entry { public Texture2D Tex; public bool Loading, Failed; public float Used; }
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        private const int MaxBytes = 2000000, MaxEntries = 40;
        private static int _running;

        /// <summary>Counts every picture that arrives or goes, so a view knows to look again.</summary>
        internal static int Version { get; private set; }

        internal static Texture2D Get(string url) { if (Entries.TryGetValue(url, out Entry e)) { e.Used = Time.unscaledTime; return e.Tex; } return null; }

        internal static Vector2? Size(string url) => Entries.TryGetValue(url, out Entry e) && e.Tex != null ? new Vector2(e.Tex.width, e.Tex.height) : (Vector2?)null;

        internal static bool Known(string url) => Entries.ContainsKey(url);

        internal static void Request(string url)
        {
            if (Entries.ContainsKey(url) || _running >= 3) return;
            Evict();
            Entries[url] = new Entry { Loading = true, Used = Time.unscaledTime };
            Plugin.Instance.StartCoroutine(Load(url));
        }

        private static void Evict()
        {
            if (Entries.Count < MaxEntries) return;
            string oldest = null; float t = float.MaxValue;
            foreach (KeyValuePair<string, Entry> e in Entries) if (!e.Value.Loading && e.Value.Used < t) { t = e.Value.Used; oldest = e.Key; }
            if (oldest == null) return;
            if (Entries[oldest].Tex != null) UnityEngine.Object.Destroy(Entries[oldest].Tex);
            Entries.Remove(oldest);
            Version++;
        }

        internal static void Clear()
        {
            foreach (Entry e in Entries.Values) if (e.Tex != null) UnityEngine.Object.Destroy(e.Tex);
            Entries.Clear();
            _running = 0;
            Version++;
        }

        private static IEnumerator Load(string start)
        {
            _running++;
            string url = start;
            Entry entry = Entries[start];
            for (int hop = 0; hop <= 4; hop++)
            {
                if (Net.Check(url, out Uri uri) != null) break;
                using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url, true))
                {
                    request.redirectLimit = 0;
                    request.timeout = 15;
                    request.SetRequestHeader("User-Agent", Net.UserAgent);
                    request.SetRequestHeader("Accept", "image/png,image/jpeg;q=0.9");
                    UnityWebRequestAsyncOperation op = request.SendWebRequest();
                    while (!op.isDone)
                    {
                        if (request.downloadedBytes > (ulong)MaxBytes) { request.Abort(); break; }
                        yield return null;
                    }
                    long code = request.responseCode;
                    if (code >= 300 && code < 400)
                    {
                        string location = request.GetResponseHeader("Location");
                        if (string.IsNullOrEmpty(location) || !Uri.TryCreate(uri, location, out Uri next)) break;
                        url = next.GetLeftPart(UriPartial.Query);
                        continue;
                    }
                    if (request.result == UnityWebRequest.Result.Success && code < 400)
                    {
                        Texture2D tex = null;
                        try { tex = DownloadHandlerTexture.GetContent(request); } catch (Exception) { }
                        if (tex != null && tex.width <= 4096 && tex.height <= 4096) { tex.name = "TerminalPicture"; entry.Tex = tex; }
                        else if (tex != null) UnityEngine.Object.Destroy(tex);
                    }
                }
                break;
            }
            entry.Loading = false;
            entry.Failed = entry.Tex == null;
            _running--;
            Version++;
        }
    }
}
