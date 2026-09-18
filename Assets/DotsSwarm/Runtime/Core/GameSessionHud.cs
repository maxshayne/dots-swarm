using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DotsSwarm.Gameplay
{
    // Construction allocates once. Numeric refreshes reuse the char buffer and
    // TMP's prewarmed text/mesh storage instead of constructing managed strings.
    public sealed class GameSessionHud : IDisposable
    {
        private readonly GameObject root;
        private readonly GameObject ownedEventSystem;
        private readonly GameObject resultPanel;
        private readonly TextMeshProUGUI resultText;
        private readonly TextMeshProUGUI stats;
        private readonly char[] buffer = new char[BenchmarkHudText.Capacity];
        private SessionStatus lastStatus = (SessionStatus)255;

        public GameSessionHud(Action<BenchmarkPreset> selectPreset, UnityAction restart)
        {
            root = new GameObject("Session HUD", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            if (EventSystem.current == null)
                ownedEventSystem = new GameObject("HUD Input", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var panel = Panel(root.transform, "Benchmark", new Vector2(16f, -16f), new Vector2(580f, 156f));
            stats = Label(panel.transform, "Counters", new Vector2(12f, -8f), new Vector2(556f, 86f), 20f);
            for (var i = 0; i < buffer.Length; i++) buffer[i] = (char)('0' + i % 10);
            stats.SetCharArray(buffer, 0, buffer.Length);
            stats.ForceMeshUpdate();
            Button(panel.transform, "0 Survival", 12f, -106f, 104f, () => selectPreset(BenchmarkPreset.Survival));
            Button(panel.transform, "1: 1k", 124f, -106f, 100f, () => selectPreset(BenchmarkPreset.Swarm1K));
            Button(panel.transform, "2: 10k", 232f, -106f, 100f, () => selectPreset(BenchmarkPreset.Swarm10K));
            Button(panel.transform, "3: 20k", 340f, -106f, 100f, () => selectPreset(BenchmarkPreset.Swarm20K));
            Button(panel.transform, "4: 50k", 448f, -106f, 120f, () => selectPreset(BenchmarkPreset.Swarm50K));

            resultPanel = Panel(root.transform, "Result", Vector2.zero, new Vector2(320f, 144f));
            var resultRect = (RectTransform)resultPanel.transform;
            resultRect.anchorMin = resultRect.anchorMax = resultRect.pivot = new Vector2(0.5f, 0.5f);
            resultText = Label(resultPanel.transform, "Outcome", new Vector2(16f, -16f), new Vector2(288f, 44f), 28f);
            resultText.alignment = TextAlignmentOptions.Center;
            Button(resultPanel.transform, "Restart (R)", 40f, -82f, 240f, restart);
            resultPanel.SetActive(false);
            root.SetActive(false);
        }

        public void SetVisible(bool visible)
        {
            if (root.activeSelf != visible) root.SetActive(visible);
        }

        public void Refresh(int enemies, int projectiles, float milliseconds,
            in GameSession session, in BenchmarkState benchmark)
        {
            var length = BenchmarkHudText.Write(buffer, enemies, projectiles, milliseconds, session, benchmark);
            stats.SetCharArray(buffer, 0, length);
            SetStatus(session.Status);
        }

        public void SetStatus(SessionStatus status)
        {
            if (lastStatus == status) return;
            lastStatus = status;
            resultText.text = status == SessionStatus.Won ? "You survived!" : "Defeat";
            resultPanel.SetActive(status != SessionStatus.Running);
        }

        private static GameObject Panel(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var obj = Rect(parent, name, position, size);
            obj.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.11f, 0.94f);
            return obj;
        }

        private static GameObject Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return obj;
        }

        private static TextMeshProUGUI Label(Transform parent, string name, Vector2 position, Vector2 size, float fontSize)
        {
            var text = Rect(parent, name, position, size).AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            return text;
        }

        private static void Button(Transform parent, string title, float x, float y, float width, UnityAction action)
        {
            var obj = Panel(parent, title, new Vector2(x, y), new Vector2(width, 36f));
            var image = obj.GetComponent<Image>();
            image.color = new Color(0.18f, 0.25f, 0.34f, 1f);
            var button = obj.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var text = Label(obj.transform, "Label", Vector2.zero, new Vector2(width, 36f), 18f);
            text.alignment = TextAlignmentOptions.Center;
            text.text = title;
        }

        public void Dispose()
        {
            Destroy(root);
            if (ownedEventSystem != null) Destroy(ownedEventSystem);
        }

        private static void Destroy(GameObject obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
