using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PredictionDebug
{
    /// <summary>
    /// Shows the prediction settings a demo is actually running with, as text in the bottom left corner,
    /// so screenshots and recordings of the four demos can be compared knowing what each was configured to do.
    /// Every library fills the same rows (see PredictionSetting); library-only knobs follow underneath.
    /// Values marked with a dagger are private or internal in the library and are read via reflection.
    /// </summary>
    public abstract class PredictionSettingsPanelBase : MonoBehaviour
    {
        [SerializeField] int fontSize = 12;
        [SerializeField] float refreshSeconds = 0.5f;
        [SerializeField] Vector2 margin = new Vector2(8f, 8f);
        [SerializeField] bool startVisible = true;
#if ENABLE_INPUT_SYSTEM
        [SerializeField] Key toggleKey = Key.F4;
#else
        [SerializeField] KeyCode toggleKey = KeyCode.F4;
#endif

        protected abstract string LibraryName { get; }

        /// <summary>Called once: register a value for every PredictionSetting (or NotApplicable) and any extras.</summary>
        protected abstract void Describe(SettingsSheet sheet);

        readonly SettingsSheet _sheet = new();
        GameObject _canvas;
        Text _labels;
        Text _values;
        float _nextRefresh;

        /// <summary>
        /// Called by each library's panel after the first scene loads: creates it unless the scene already has one.
        /// Define PREDICTION_SETTINGS_NO_AUTOSPAWN in Player Settings to place the component by hand instead.
        /// </summary>
        protected static void SpawnIfMissing<T>() where T : PredictionSettingsPanelBase
        {
#if !PREDICTION_SETTINGS_NO_AUTOSPAWN
            if (FindAnyObjectByType<PredictionSettingsPanelBase>(FindObjectsInactive.Include) != null)
                return;
            var go = new GameObject("PredictionSettingsPanel (auto)");
            DontDestroyOnLoad(go);
            go.AddComponent<T>();
#endif
        }

        protected virtual void Awake()
        {
            Describe(_sheet);
            BuildUI();
            _canvas.SetActive(startVisible);
        }

        protected virtual void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas);
        }

        protected virtual void Update()
        {
            if (TogglePressed())
                _canvas.SetActive(!_canvas.activeSelf);

            if (!_canvas.activeSelf || Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + refreshSeconds;
            _sheet.Refresh();
            Render();
        }

        bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame;
#else
            return Input.GetKeyDown(toggleKey);
#endif
        }

        void Render()
        {
            var labels = new StringBuilder();
            var values = new StringBuilder();
            bool anyReflection = false;

            labels.Append("<b>").Append(LibraryName).Append("</b>\n");
            values.Append("<color=#888888>").Append(KeyName()).Append("</color>\n");

            foreach (var (title, first, last) in SettingsSheet.Sections)
            {
                AppendSection(labels, values, title);
                for (var s = first; s <= last; s++)
                {
                    if (!_sheet.Standard.TryGetValue(s, out var row))
                        row = new SettingsSheet.Row { Label = SettingsSheet.LabelOf(s), Cached = SettingsSheet.None };
                    AppendRow(labels, values, row, ref anyReflection);
                }
            }

            if (_sheet.Extras.Count > 0)
            {
                AppendSection(labels, values, LibraryName + " only");
                foreach (var row in _sheet.Extras)
                    AppendRow(labels, values, row, ref anyReflection);
            }

            if (anyReflection)
            {
                labels.Append("<color=#888888>† reflection</color>\n");
                values.Append('\n');
            }

            _labels.text = labels.ToString();
            _values.text = values.ToString();
        }

        static void AppendSection(StringBuilder labels, StringBuilder values, string title)
        {
            labels.Append("<b><color=#9fd3ff>").Append(title).Append("</color></b>\n");
            values.Append('\n');
        }

        static void AppendRow(StringBuilder labels, StringBuilder values, SettingsSheet.Row row, ref bool anyReflection)
        {
            labels.Append("  ").Append(row.Label).Append('\n');
            bool notApplicable = row.Cached == SettingsSheet.None;
            if (notApplicable)
                values.Append("<color=#888888>");
            values.Append(row.Cached);
            if (row.ViaReflection)
            {
                values.Append(" †");
                anyReflection = true;
            }
            if (notApplicable)
                values.Append("</color>");
            values.Append('\n');
        }

        string KeyName() => toggleKey.ToString();

        void BuildUI()
        {
            _canvas = new GameObject("PredictionSettingsCanvas", typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(_canvas);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31999; // just under the ResimGraph overlay
            _canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            // Background sized to the text: a vertical layout with content size fitting around the two columns.
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(_canvas.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = margin;
            var image = panel.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.6f);
            image.raycastTarget = false;
            var layout = panel.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _labels = CreateColumn(panel.transform, "Labels", new Color(0.85f, 0.85f, 0.85f));
            _values = CreateColumn(panel.transform, "Values", Color.white);
        }

        Text CreateColumn(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
