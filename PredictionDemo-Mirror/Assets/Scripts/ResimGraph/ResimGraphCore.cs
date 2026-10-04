// Shared part of the ResimGraph debug overlay. This file is identical in all four demo projects;
// only ResimGraph.cs differs, because it is the part that reads each prediction library.
// See ResimGraph.md in the repository root for the setup and the meaning of every graph.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PredictionDebug
{
    public enum StripKind
    {
        /// <summary>One vertical line per frame, height proportional to the value.</summary>
        Bar,
        /// <summary>A full height line on frames where the event happened, coloured by severity.</summary>
        Event,
        /// <summary>Signed value drawn up or down from a midline.</summary>
        Bipolar,
    }

    /// <summary>Where the numbers on a graph come from. Shown as a prefix on its label.</summary>
    public enum MetricSupport
    {
        /// <summary>Read straight from a counter, event or property the library exposes.</summary>
        Native,
        /// <summary>The library exposes the inputs, the graph does the arithmetic (label prefix "*").</summary>
        Computed,
        /// <summary>The library has no such metric, the graph shows the closest one it has (label prefix "~").</summary>
        Substitute,
        /// <summary>Nothing comparable is reachable through the public API (label shows "n/a").</summary>
        Unsupported,
    }

    /// <summary>
    /// One scrolling graph. Values pushed during a frame are folded into a single column when the
    /// frame is drawn: <see cref="Max"/> keeps the worst sample, <see cref="Add"/> sums them,
    /// <see cref="Set"/> keeps the last one and <see cref="Event"/> counts occurrences.
    /// </summary>
    public sealed class GraphStrip
    {
        public readonly string Id;
        public string Title;
        public string Unit;
        public string Format = "0.0";
        public StripKind Kind;
        public Color Low;
        public Color High;
        /// <summary>Value that fills the strip (Bipolar: fills one half).</summary>
        public float Scale = 1f;
        /// <summary>When above zero, colour follows the distance from this value instead of the value itself.</summary>
        public float ColorCenter;
        /// <summary>Events in one frame that map to the hottest colour when no severity is given.</summary>
        public float EventCountScale = 4f;
        /// <summary>Horizontal reference line every GridStep value units. 0 disables the grid.</summary>
        public float GridStep;
        public MetricSupport Support = MetricSupport.Native;
        /// <summary>What the graph measures in this library, or why it can't. Logged once and listed in the report.</summary>
        public string Note = "";
        /// <summary>True while the metric doesn't apply to the current role, e.g. a server only metric on a client.</summary>
        public bool Inactive;
        public string InactiveReason = "";

        internal RawImage Image;
        internal Text Label;
        internal int Row;

        float _acc;
        float _colorT = float.NaN;
        bool _has;
        float[] _values;
        float[] _frameDts;
        float _lastValue = float.NaN;

        public GraphStrip(string id, string title, string unit, StripKind kind, Color low, Color high)
        {
            Id = id;
            Title = title;
            Unit = unit;
            Kind = kind;
            Low = low;
            High = high;
        }

        public bool IsShown => Support != MetricSupport.Unsupported;

        public GraphStrip MarkUnsupported(string why)
        {
            Support = MetricSupport.Unsupported;
            Note = why;
            return this;
        }

        public GraphStrip MarkComputed(string how)
        {
            Support = MetricSupport.Computed;
            Note = how;
            return this;
        }

        /// <summary>Shows a similar metric in place of the requested one, under a title that says what it really is.</summary>
        public GraphStrip MarkSubstitute(string title, string what)
        {
            Support = MetricSupport.Substitute;
            Title = title;
            Note = what;
            return this;
        }

        public GraphStrip WithNote(string note)
        {
            Note = note;
            return this;
        }

        public void Max(float value, float colorT = float.NaN)
        {
            if (!_has || value > _acc)
                _acc = value;
            if (!float.IsNaN(colorT))
                _colorT = float.IsNaN(_colorT) ? colorT : Mathf.Max(_colorT, colorT);
            _has = true;
        }

        public void Add(float value)
        {
            _acc = _has ? _acc + value : value;
            _has = true;
        }

        public void Set(float value)
        {
            _acc = value;
            _has = true;
        }

        /// <param name="severity">0..1 colour position, NaN to colour by how many events landed in the frame.</param>
        public void Event(float severity = float.NaN)
        {
            _acc = _has ? _acc + 1f : 1f;
            if (!float.IsNaN(severity))
                _colorT = float.IsNaN(_colorT) ? severity : Mathf.Max(_colorT, severity);
            _has = true;
        }

        internal void Allocate(int width)
        {
            _values = new float[width];
            _frameDts = new float[width];
            for (int i = 0; i < width; i++)
                _values[i] = float.NaN;
        }

        /// <summary>Folds this frame's samples into column x and clears them for the next frame.</summary>
        internal void Commit(int x, float dt, Color32[] column, int height, Color32 background, Color32 gridOverBackground)
        {
            float value = (_has && !Inactive) ? _acc : float.NaN;
            float colorT = _colorT;
            _has = false;
            _acc = 0f;
            _colorT = float.NaN;

            _values[x] = value;
            _frameDts[x] = dt;
            if (!float.IsNaN(value))
                _lastValue = value;

            // Height of the line in texels, and for bipolar graphs which side of the midline it grows to.
            int from = 0;
            int to = 0;
            if (!float.IsNaN(value))
            {
                switch (Kind)
                {
                    case StripKind.Event:
                        if (value > 0f)
                        {
                            to = height;
                            if (float.IsNaN(colorT))
                                colorT = EventCountScale > 1f ? (value - 1f) / (EventCountScale - 1f) : 1f;
                        }
                        break;

                    case StripKind.Bipolar:
                    {
                        int mid = height / 2;
                        float ratio = Scale > 0f ? Mathf.Clamp(value / Scale, -1f, 1f) : 0f;
                        int extent = Mathf.RoundToInt(Mathf.Abs(ratio) * mid);
                        if (ratio >= 0f) { from = mid; to = mid + Mathf.Max(extent, 1); }
                        else { from = mid - extent; to = mid; }
                        if (float.IsNaN(colorT))
                            colorT = Mathf.Abs(ratio);
                        break;
                    }

                    default:
                    {
                        float ratio = Scale > 0f ? value / Scale : 0f;
                        to = Mathf.Clamp(Mathf.RoundToInt(ratio * height), value > 0f ? 1 : 0, height);
                        if (float.IsNaN(colorT))
                            colorT = ColorCenter > 0f ? Mathf.Abs(value - ColorCenter) / ColorCenter : ratio;
                        break;
                    }
                }
            }

            Color barColor = Color.Lerp(Low, High, Mathf.Clamp01(float.IsNaN(colorT) ? 0f : colorT));
            var bar = (Color32)barColor;
            // Inside a bar the grid darkens the bar instead of lightening the background, otherwise it
            // disappears against the brighter colours at the top of the scale.
            var gridOverBar = (Color32)Color.Lerp(barColor, new Color(0f, 0f, 0f, barColor.a), 0.22f);

            for (int y = 0; y < height; y++)
            {
                bool grid = IsGridRow(y, height);
                bool inBar = y >= from && y < to;
                column[y] = inBar ? (grid ? gridOverBar : bar) : (grid ? gridOverBackground : background);
            }
        }

        internal bool IsGridRow(int y, int height)
        {
            if (y == 0)
                return false;

            if (Kind == StripKind.Bipolar)
            {
                if (y == height / 2)
                    return true;
                if (GridStep <= 0f || Scale <= 0f)
                    return false;
                int mid = height / 2;
                float stepTexels = GridStep / Scale * mid;
                if (stepTexels < 2f)
                    return false;
                float offset = Mathf.Abs(y - mid) / stepTexels;
                return Mathf.Abs(offset - Mathf.Round(offset)) * stepTexels < 0.5f;
            }

            if (GridStep <= 0f || Scale <= 0f)
                return false;
            float step = GridStep / Scale * height;
            if (step < 2f)
                return false;
            float f = y / step;
            return Mathf.Abs(f - Mathf.Round(f)) * step < 0.5f && y < height;
        }

        internal string BuildLabel()
        {
            string prefix = Support == MetricSupport.Computed ? "*" : Support == MetricSupport.Substitute ? "~" : "";
            if (Inactive)
                return $"{prefix}{Title}  {InactiveReason}";

            switch (Kind)
            {
                case StripKind.Event:
                {
                    float count = 0f;
                    float span = 0f;
                    for (int i = 0; i < _values.Length; i++)
                    {
                        span += _frameDts[i];
                        if (!float.IsNaN(_values[i]))
                            count += _values[i];
                    }
                    float rate = span > 0f ? count / span : 0f;
                    return $"{prefix}{Title}  {rate:0.0}/s  ({count:0} shown)";
                }

                case StripKind.Bipolar:
                {
                    float lo = float.NaN, hi = float.NaN;
                    for (int i = 0; i < _values.Length; i++)
                    {
                        float v = _values[i];
                        if (float.IsNaN(v)) continue;
                        lo = float.IsNaN(lo) ? v : Mathf.Min(lo, v);
                        hi = float.IsNaN(hi) ? v : Mathf.Max(hi, v);
                    }
                    if (float.IsNaN(_lastValue))
                        return $"{prefix}{Title}  -";
                    return $"{prefix}{Title}  {_lastValue.ToString("+" + Format + ";-" + Format)}{Unit}  [{lo.ToString(Format)}..{hi.ToString(Format)}]";
                }

                default:
                {
                    float peak = float.NaN;
                    for (int i = 0; i < _values.Length; i++)
                    {
                        float v = _values[i];
                        if (!float.IsNaN(v) && (float.IsNaN(peak) || v > peak))
                            peak = v;
                    }
                    if (float.IsNaN(_lastValue))
                        return $"{prefix}{Title}  -";
                    return $"{prefix}{Title}  {_lastValue.ToString(Format)}{Unit}  pk {peak.ToString(Format)}";
                }
            }
        }
    }

    /// <summary>
    /// Builds the overlay, stacks the graphs into columns and draws one column per rendered frame.
    /// Subclasses only declare what their library supports and push values into the strips.
    /// Collection runs in LateUpdate at a very late execution order, so the frame's simulation,
    /// network receive and visual smoothing have all happened before it is sampled.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public abstract class ResimGraphBase : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Canvas to draw into. Left empty, the graph creates its own screen space overlay canvas.")]
        [SerializeField] protected Canvas parentCanvas;
        [SerializeField] int width = 300;
        [SerializeField] int stripHeight = 26;
        [SerializeField] int gap = 2;
        [SerializeField] int columnGap = 8;
        [SerializeField] Vector2 margin = new Vector2(8f, 8f);
        [Tooltip("Graphs the library can't feed still get a row with an n/a label, so the four demos line up side by side.")]
        [SerializeField] bool showUnsupported = true;
        [SerializeField] bool startVisible = true;
#if ENABLE_INPUT_SYSTEM
        [SerializeField] Key toggleKey = Key.F3;
#else
        [SerializeField] KeyCode toggleKey = KeyCode.F3;
#endif

        [Header("Scales (the value that fills a graph)")]
        [SerializeField] protected int resimDepthScaleTicks = 30;
        [Tooltip("0 uses one fixed timestep.")]
        [SerializeField] protected float resimCostScaleMs = 0f;
        [SerializeField] protected int resimEntitiesScale = 20;
        [SerializeField] protected float errorScaleMeters = 0.5f;
        [SerializeField] protected float errorScaleDegrees = 15f;
        [Tooltip("0 uses one fixed timestep.")]
        [SerializeField] protected float tickCostScaleMs = 0f;
        [SerializeField] protected float clockAdjustScalePercent = 2f;
        [SerializeField] protected float latencyScaleMs = 400f;
        [SerializeField] protected int tickLeadScaleTicks = 30;
        [SerializeField] protected float snapshotAgeScaleMs = 100f;
        [SerializeField] protected float inputBufferScaleTicks = 10f;
        [SerializeField] protected float packetLossScale = 5f;
        [SerializeField] protected float bandwidthScaleKBps = 64f;
        [SerializeField] protected float smoothingScaleMeters = 1f;

        [Header("Visual jump (same defaults as Ursitoare's PredictedEntityVisuals)")]
        [SerializeField] protected float jumpThresholdMeters = 0.35f;
        [SerializeField] protected float jumpThresholdDegrees = 2.5f;
        [SerializeField] protected float jumpScaleMeters = 1f;
        [SerializeField] protected float jumpScaleDegrees = 20f;

        // The standard set, in display order. Every library gets every row so the four demos can be
        // compared line by line; the ones a library can't feed are marked in Configure().
        protected GraphStrip Resim;
        protected GraphStrip ResimDepth;
        protected GraphStrip ResimCost;
        protected GraphStrip ResimEntities;
        protected GraphStrip PredictionError;
        protected GraphStrip Correction;
        protected GraphStrip TickCost;
        protected GraphStrip TickGap;
        protected GraphStrip ClockAdjust;
        protected GraphStrip Latency;
        protected GraphStrip TickLead;
        protected GraphStrip SnapshotAge;
        protected GraphStrip InputBuffer;
        protected GraphStrip PacketLoss;
        protected GraphStrip NetIn;
        protected GraphStrip NetOut;
        protected GraphStrip VisualJump;
        protected GraphStrip Smoothing;

        readonly List<GraphStrip> _strips = new List<GraphStrip>();
        readonly List<GraphStrip> _shown = new List<GraphStrip>();

        GameObject _root;
        bool _ownsCanvas;
        Texture2D _atlas;
        Color32[] _column;
        Text _header;
        int _writeHead;
        float _nextLabelTime;
        Vector2 _laidOutFor = new Vector2(-1f, -1f);
        bool _subscribed;
        bool _visible;

        static readonly Color32 Background = new Color32(0, 0, 0, 100);
        static readonly Color32 GridOverBackground = new Color32(255, 255, 255, 22);
        static readonly Color32 Cursor = new Color32(255, 255, 255, 60);

        /// <summary>Name shown in the header line.</summary>
        protected abstract string LibraryName { get; }

        /// <summary>Marks what the library supports and adds library specific graphs. Runs once, before the UI is built.</summary>
        protected abstract void Configure();

        /// <summary>Hooks into the library. Called every frame until it returns true, since most managers only exist once networking has started.</summary>
        protected abstract bool TrySubscribe();

        protected abstract void Unsubscribe();

        /// <summary>Pushes this frame's values. Runs every frame after a successful subscribe.</summary>
        protected abstract void Collect();

        /// <summary>Lets the library drop its hooks when the session it subscribed to has gone away, so the next one is picked up.</summary>
        protected virtual bool IsSubscriptionStale() => false;

        /// <summary>Length of one simulation tick. Libraries with their own tick clock override it.</summary>
        protected virtual float TickStepMs => Time.fixedDeltaTime * 1000f;

        float _scaledForStepMs = -1f;

        protected GraphStrip AddStrip(string id, string title, string unit, StripKind kind, Color low, Color high, float scale, string format = "0.0")
        {
            var strip = new GraphStrip(id, title, unit, kind, low, high) { Scale = scale, Format = format };
            _strips.Add(strip);
            return strip;
        }

        protected virtual void Awake()
        {
            CreateStandardStrips();
            Configure();
            LogSupport();
        }

        protected virtual void OnEnable()
        {
            if (_root == null)
                BuildUI();
            _visible = startVisible;
            _root.SetActive(_visible);
        }

        protected virtual void OnDisable()
        {
            if (_subscribed)
                Unsubscribe();
            _subscribed = false;
            if (_root != null)
                _root.SetActive(false);
        }

        protected virtual void OnDestroy()
        {
            if (_root != null)
            {
                // A canvas the graph created goes with it; on a borrowed canvas only our own objects do.
                Destroy(_ownsCanvas ? _root.transform.parent.gameObject : _root);
            }
            if (_atlas != null)
                Destroy(_atlas);
        }

        /// <summary>
        /// The demos set their tick rate when networking starts, after this component woke up, so the
        /// graphs scaled by one tick follow the live value and repaint their grid when it changes.
        /// </summary>
        void RefreshTickScales()
        {
            float step = TickStepMs;
            if (step <= 0f || Mathf.Abs(step - _scaledForStepMs) < 0.001f)
                return;
            _scaledForStepMs = step;

            if (resimCostScaleMs <= 0f)
                ResimCost.Scale = step;
            if (tickCostScaleMs <= 0f)
                TickCost.Scale = step;
            TickGap.Scale = step * 2f;
            TickGap.ColorCenter = step;
            TickGap.GridStep = step;
            SnapshotAge.GridStep = step * 4f;

            if (_atlas == null)
                return;
            foreach (var s in new[] { ResimCost, TickCost, TickGap, SnapshotAge })
                if (s.IsShown && _shown.Contains(s))
                    RepaintStrip(s);
            _atlas.Apply(false);
        }

        void RepaintStrip(GraphStrip s)
        {
            for (int x = 0; x < width; x++)
                for (int y = 0; y < stripHeight; y++)
                    _atlas.SetPixel(x, s.Row * stripHeight + y, s.IsGridRow(y, stripHeight) ? GridOverBackground : Background);
        }

        void CreateStandardStrips()
        {
            float step = TickStepMs;

            Resim = AddStrip("resim", "RESIM", "", StripKind.Event,
                new Color(1f, 0.25f, 0.25f), new Color(1f, 0.85f, 0.85f), 1f);

            ResimDepth = AddStrip("resim_depth", "RESIM DEPTH", "t", StripKind.Bar,
                new Color(1f, 0.60f, 0.20f), new Color(0.85f, 0.05f, 0.05f), resimDepthScaleTicks, "0");
            ResimDepth.GridStep = 10f;

            ResimCost = AddStrip("resim_cost", "RESIM COST", "ms", StripKind.Bar,
                new Color(1f, 0.55f, 0.35f), new Color(1f, 0.10f, 0.40f), resimCostScaleMs > 0f ? resimCostScaleMs : step, "0.00");

            ResimEntities = AddStrip("resim_entities", "RESIM ENTITIES", "", StripKind.Bar,
                new Color(0.95f, 0.75f, 0.30f), new Color(1f, 0.30f, 0.10f), resimEntitiesScale, "0");

            PredictionError = AddStrip("prediction_error", "PREDICTION ERROR", "m", StripKind.Bar,
                new Color(1f, 0.45f, 0.70f), new Color(1f, 0.05f, 0.30f), errorScaleMeters, "0.000");

            Correction = AddStrip("correction", "CORRECTION", "m", StripKind.Bar,
                new Color(0.85f, 0.55f, 1f), new Color(1f, 0.10f, 0.60f), errorScaleMeters, "0.000");

            TickCost = AddStrip("tick_cost", "TICK COST", "ms", StripKind.Bar,
                new Color(0.24f, 0.86f, 0.35f), new Color(1f, 0.16f, 0.16f), tickCostScaleMs > 0f ? tickCostScaleMs : step, "0.00");

            TickGap = AddStrip("tick_gap", "TICK GAP", "ms", StripKind.Bar,
                new Color(0.30f, 0.65f, 1f), new Color(1f, 0.70f, 0.10f), step * 2f, "0.0");
            TickGap.ColorCenter = step;
            TickGap.GridStep = step;

            ClockAdjust = AddStrip("clock_adjust", "CLOCK ADJUST", "%", StripKind.Bipolar,
                new Color(0.55f, 0.85f, 0.85f), new Color(1f, 0.55f, 0.10f), clockAdjustScalePercent, "0.00");
            ClockAdjust.GridStep = clockAdjustScalePercent / 2f;

            Latency = AddStrip("latency", "LATENCY RTT", "ms", StripKind.Bar,
                new Color(0.60f, 0.50f, 1f), new Color(1f, 0.35f, 0.15f), latencyScaleMs, "0");
            Latency.GridStep = 50f;

            TickLead = AddStrip("tick_lead", "TICK LEAD", "t", StripKind.Bar,
                new Color(0.45f, 0.80f, 0.95f), new Color(0.95f, 0.30f, 0.80f), tickLeadScaleTicks, "0");
            TickLead.GridStep = 10f;

            SnapshotAge = AddStrip("snapshot_age", "SNAPSHOT AGE", "ms", StripKind.Bar,
                new Color(0.55f, 0.75f, 0.55f), new Color(1f, 0.40f, 0.20f), snapshotAgeScaleMs, "0");
            SnapshotAge.GridStep = step * 4f;

            InputBuffer = AddStrip("input_buffer", "SERVER INPUT BUFFER", "t", StripKind.Bar,
                new Color(0.40f, 0.90f, 0.70f), new Color(1f, 0.85f, 0.20f), inputBufferScaleTicks, "0");
            InputBuffer.GridStep = 1f;

            PacketLoss = AddStrip("packet_loss", "PACKET LOSS", "", StripKind.Event,
                new Color(1f, 0.92f, 0.20f), new Color(1f, 0.10f, 0.85f), 1f);
            PacketLoss.EventCountScale = packetLossScale;

            NetIn = AddStrip("net_in", "NET IN", "KB/s", StripKind.Bar,
                new Color(0.35f, 0.75f, 0.95f), new Color(0.20f, 0.30f, 1f), bandwidthScaleKBps, "0.0");
            NetIn.GridStep = bandwidthScaleKBps / 4f;

            NetOut = AddStrip("net_out", "NET OUT", "KB/s", StripKind.Bar,
                new Color(0.95f, 0.70f, 0.35f), new Color(1f, 0.30f, 0.20f), bandwidthScaleKBps, "0.0");
            NetOut.GridStep = bandwidthScaleKBps / 4f;

            VisualJump = AddStrip("visual_jump", "VISUAL JUMP", "", StripKind.Event,
                new Color(0.25f, 0.95f, 0.95f), new Color(1f, 1f, 1f), 1f);

            Smoothing = AddStrip("smoothing", "VISUAL vs SIM", "m", StripKind.Bar,
                new Color(0.70f, 0.90f, 0.40f), new Color(1f, 0.50f, 0.10f), smoothingScaleMeters, "0.000");
        }

        void LogSupport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[ResimGraph][{LibraryName}] graph support:");
            foreach (var s in _strips)
                sb.AppendLine($"  {s.Id,-18} {s.Support,-11} {s.Title}: {s.Note}");
            Debug.Log(sb.ToString());
        }

        void BuildUI()
        {
            Transform parent;
            if (parentCanvas != null)
            {
                parent = parentCanvas.transform;
            }
            else
            {
                var canvasGO = new GameObject("ResimGraphCanvas", typeof(Canvas), typeof(CanvasScaler));
                var canvas = canvasGO.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;
                canvasGO.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                // Kept out of the scene hierarchy of whatever it sits on, and alive across scene loads with it.
                canvasGO.transform.SetParent(transform, false);
                parentCanvas = canvas;
                parent = canvasGO.transform;
                _ownsCanvas = true;
            }

            _root = new GameObject("ResimGraph", typeof(RectTransform));
            _root.transform.SetParent(parent, false);
            var rootRect = (RectTransform)_root.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            _shown.Clear();
            foreach (var s in _strips)
                if (s.IsShown || showUnsupported)
                    _shown.Add(s);

            int atlasHeight = Mathf.Max(1, _shown.Count * stripHeight);
            _atlas = new Texture2D(width, atlasHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "ResimGraphAtlas",
            };
            var clear = new Color32[width * atlasHeight];
            for (int i = 0; i < clear.Length; i++)
                clear[i] = Background;
            _atlas.SetPixels32(clear);
            _column = new Color32[stripHeight];

            _header = CreateText(_root.transform, "Header", new Color(1f, 1f, 1f, 0.9f), 10, TextAnchor.LowerLeft);
            _header.text = $"{LibraryName}   * computed by the graph   ~ closest substitute   {toggleKey}: hide";

            for (int i = 0; i < _shown.Count; i++)
            {
                var s = _shown[i];
                s.Row = i;
                s.Allocate(width);

                var go = new GameObject(s.Id, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                go.transform.SetParent(_root.transform, false);
                s.Image = go.GetComponent<RawImage>();
                s.Image.texture = _atlas;
                s.Image.raycastTarget = false;
                s.Image.uvRect = new Rect(0f, (float)(i * stripHeight) / atlasHeight, 1f, (float)stripHeight / atlasHeight);

                bool shown = s.IsShown;
                Color labelColor = shown ? s.Low : new Color(0.6f, 0.6f, 0.6f, 0.8f);
                s.Label = CreateText(go.transform, "Label", labelColor, 9, TextAnchor.UpperLeft);
                s.Label.text = shown ? s.Title : $"{s.Title}  n/a";

                // Grid lines are painted across the whole strip right away, so the scale reads before it fills.
                if (shown)
                    RepaintStrip(s);
            }

            _atlas.Apply(false);
            _scaledForStepMs = TickStepMs;
            Layout(true);
        }

        static Text CreateText(Transform parent, string name, Color color, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 1f);
            rt.offsetMax = new Vector2(-4f, -1f);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>Stacks the strips top down from the top left corner and starts a new column when the screen runs out.</summary>
        void Layout(bool force)
        {
            float scale = parentCanvas != null ? Mathf.Max(0.01f, parentCanvas.scaleFactor) : 1f;
            var screen = new Vector2(Screen.width / scale, Screen.height / scale);
            if (!force && screen == _laidOutFor)
                return;
            _laidOutFor = screen;

            const float headerHeight = 14f;
            var headerRect = _header.rectTransform;
            headerRect.anchorMin = headerRect.anchorMax = headerRect.pivot = new Vector2(0f, 1f);
            headerRect.sizeDelta = new Vector2(width * 2f, headerHeight);
            headerRect.anchoredPosition = new Vector2(margin.x, -margin.y);

            float top = margin.y + headerHeight + gap;
            int rowsPerColumn = Mathf.Max(1, Mathf.FloorToInt((screen.y - top - margin.y + gap) / (stripHeight + gap)));

            for (int i = 0; i < _shown.Count; i++)
            {
                int column = i / rowsPerColumn;
                int row = i % rowsPerColumn;
                var rt = _shown[i].Image.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(width, stripHeight);
                rt.anchoredPosition = new Vector2(margin.x + column * (width + columnGap), -(top + row * (stripHeight + gap)));
            }
        }

        protected virtual void LateUpdate()
        {
            if (_root == null)
                return;

            if (WasTogglePressed())
            {
                _visible = !_visible;
                _root.SetActive(_visible);
            }

            RefreshTickScales();

            if (_subscribed && IsSubscriptionStale())
            {
                Unsubscribe();
                _subscribed = false;
            }
            if (!_subscribed)
                _subscribed = TrySubscribe();
            if (_subscribed)
                Collect();

            int next = (_writeHead + 1) % width;
            float dt = Time.unscaledDeltaTime;
            foreach (var s in _shown)
            {
                if (!s.IsShown)
                    continue;

                s.Commit(_writeHead, dt, _column, stripHeight, Background, GridOverBackground);
                _atlas.SetPixels32(_writeHead, s.Row * stripHeight, 1, stripHeight, _column);

                // A dim cursor on the next column shows which way the graph scrolls.
                for (int y = 0; y < stripHeight; y++)
                    _column[y] = Cursor;
                _atlas.SetPixels32(next, s.Row * stripHeight, 1, stripHeight, _column);
            }
            _writeHead = next;

            if (!_visible)
                return;

            _atlas.Apply(false);
            Layout(false);

            if (Time.unscaledTime >= _nextLabelTime)
            {
                _nextLabelTime = Time.unscaledTime + 0.2f;
                foreach (var s in _shown)
                    if (s.IsShown)
                        s.Label.text = s.BuildLabel();
            }
        }

        bool WasTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && toggleKey != Key.None && keyboard[toggleKey].wasPressedThisFrame;
#else
            return Input.GetKeyDown(toggleKey);
#endif
        }

        // ------------------------------------------------------------------------------------------
        // Shared helpers for the library specific collectors.
        // ------------------------------------------------------------------------------------------

        /// <summary>Severity of a pose difference against the error colour scales, 1 meaning at the scale max.</summary>
        protected float ErrorSeverity(float meters, float degrees)
        {
            float p = errorScaleMeters > 0f ? meters / errorScaleMeters : 0f;
            float a = errorScaleDegrees > 0f ? degrees / errorScaleDegrees : 0f;
            return Mathf.Max(p, a);
        }

        /// <summary>Plots a pose difference: height is the position error, colour the worse of position and rotation.</summary>
        protected void PushPoseError(GraphStrip strip, Vector3 a, Quaternion ar, Vector3 b, Quaternion br)
        {
            float meters = (a - b).magnitude;
            float degrees = Quaternion.Angle(ar, br);
            strip.Max(meters, ErrorSeverity(meters, degrees));
        }

        /// <summary>
        /// Same test as Ursitoare's PredictedEntityVisuals: a rendered transform that moves more than the
        /// thresholds in one frame counts as a visual jump. Used for the libraries that have no such event.
        /// </summary>
        protected sealed class VisualJumpDetector
        {
            Transform _target;
            Vector3 _lastPos;
            Quaternion _lastRot;

            public void Sample(ResimGraphBase graph, Transform rendered)
            {
                if (rendered == null)
                {
                    _target = null;
                    return;
                }

                rendered.GetPositionAndRotation(out var pos, out var rot);
                if (rendered != _target)
                {
                    _target = rendered;
                    _lastPos = pos;
                    _lastRot = rot;
                    return;
                }

                float meters = (pos - _lastPos).magnitude;
                float degrees = Quaternion.Angle(rot, _lastRot);
                _lastPos = pos;
                _lastRot = rot;

                if (meters > graph.jumpThresholdMeters || degrees > graph.jumpThresholdDegrees)
                {
                    float p = graph.jumpScaleMeters > 0f ? meters / graph.jumpScaleMeters : 1f;
                    float a = graph.jumpScaleDegrees > 0f ? degrees / graph.jumpScaleDegrees : 1f;
                    graph.VisualJump.Event(Mathf.Max(p, a));
                }
            }
        }

        /// <summary>
        /// Wall clock bookkeeping for a tick loop: cost of each tick, gap between tick starts, and the
        /// tick rate actually achieved over the last two seconds (used as the clock adjustment substitute
        /// for libraries that don't expose their own time scaling).
        /// </summary>
        protected sealed class TickClock
        {
            const double RateWindowSeconds = 2.0;
            readonly Queue<double> _starts = new Queue<double>();
            double _tickStart = -1.0;
            double _lastStart = -1.0;
            double _firstStart = -1.0;

            public static double NowSeconds => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

            public void Begin(GraphStrip gapStrip)
            {
                double now = NowSeconds;
                if (_lastStart >= 0.0)
                    gapStrip?.Max((float)((now - _lastStart) * 1000.0));
                _lastStart = now;
                _tickStart = now;
                if (_firstStart < 0.0)
                    _firstStart = now;

                _starts.Enqueue(now);
                while (_starts.Count > 0 && now - _starts.Peek() > RateWindowSeconds)
                    _starts.Dequeue();
            }

            /// <param name="excludeMs">Time spent inside the tick on work graphed separately, such as a resimulation.</param>
            public void End(GraphStrip costStrip, double excludeMs = 0.0)
            {
                if (_tickStart < 0.0)
                    return;
                double ms = (NowSeconds - _tickStart) * 1000.0 - excludeMs;
                costStrip?.Max((float)Math.Max(0.0, ms));
                _tickStart = -1.0;
            }

            /// <summary>Signed percentage the measured tick rate runs above (+) or below (-) the nominal rate. NaN until the window has filled.</summary>
            public float RateDeviationPercent(double nominalHz)
            {
                if (_firstStart < 0.0 || nominalHz <= 0.0 || NowSeconds - _firstStart < RateWindowSeconds + 0.5)
                    return float.NaN;
                double measured = _starts.Count / RateWindowSeconds;
                return (float)((measured / nominalHz - 1.0) * 100.0);
            }

            public void Reset()
            {
                _starts.Clear();
                _tickStart = _lastStart = _firstStart = -1.0;
            }
        }

        /// <summary>Bytes per second over a sliding one second window, in ten buckets.</summary>
        protected sealed class ByteRate
        {
            const int Buckets = 10;
            const double BucketSeconds = 0.1;
            readonly long[] _bytes = new long[Buckets];
            long _bucketIndex = -1;

            public void Add(int bytes)
            {
                Advance();
                _bytes[_bucketIndex % Buckets] += bytes;
            }

            public float KBps()
            {
                Advance();
                long sum = 0;
                for (int i = 0; i < Buckets; i++)
                    sum += _bytes[i];
                return sum / 1024f;
            }

            void Advance()
            {
                long index = (long)(Time.unscaledTimeAsDouble / BucketSeconds);
                if (_bucketIndex < 0)
                    _bucketIndex = index;
                while (_bucketIndex < index)
                {
                    _bucketIndex++;
                    _bytes[_bucketIndex % Buckets] = 0;
                    if (index - _bucketIndex > Buckets)
                        _bucketIndex = index - Buckets;
                }
            }
        }

        /// <summary>Poses of one body keyed by tick, for comparing a prediction against the server state for the same tick.</summary>
        protected sealed class PoseHistory
        {
            readonly ulong[] _ticks;
            readonly Vector3[] _positions;
            readonly Quaternion[] _rotations;
            readonly bool[] _set;

            public PoseHistory(int capacity = 1024)
            {
                _ticks = new ulong[capacity];
                _positions = new Vector3[capacity];
                _rotations = new Quaternion[capacity];
                _set = new bool[capacity];
            }

            public void Record(ulong tick, Vector3 position, Quaternion rotation)
            {
                int i = (int)(tick % (ulong)_ticks.Length);
                _ticks[i] = tick;
                _positions[i] = position;
                _rotations[i] = rotation;
                _set[i] = true;
            }

            public bool TryGet(ulong tick, out Vector3 position, out Quaternion rotation)
            {
                int i = (int)(tick % (ulong)_ticks.Length);
                if (_set[i] && _ticks[i] == tick)
                {
                    position = _positions[i];
                    rotation = _rotations[i];
                    return true;
                }
                position = default;
                rotation = Quaternion.identity;
                return false;
            }

            public void Clear() => Array.Clear(_set, 0, _set.Length);
        }
    }

    /// <summary>
    /// Adds callbacks to Unity's player loop, before and after a phase or one system inside it. Used to
    /// time work the libraries do outside any event they expose, such as Mirror's network receive.
    /// Each hook is identified by a marker type, which is also how it is removed again.
    /// </summary>
    public static class PlayerLoopHooks
    {
        /// <param name="phase">Top level phase, e.g. typeof(UnityEngine.PlayerLoop.FixedUpdate).</param>
        /// <param name="around">System inside the phase to wrap, or null to wrap the whole phase.</param>
        /// <returns>False when the phase or the wrapped system isn't in the player loop.</returns>
        public static bool Insert(Type phase, Type around, Type beforeMarker, PlayerLoopSystem.UpdateFunction before, Type afterMarker, PlayerLoopSystem.UpdateFunction after)
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            if (root.subSystemList == null)
                return false;

            for (int p = 0; p < root.subSystemList.Length; p++)
            {
                if (root.subSystemList[p].type != phase)
                    continue;

                var systems = new List<PlayerLoopSystem>(root.subSystemList[p].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                int beforeIndex = 0;
                int afterIndex = systems.Count;
                if (around != null)
                {
                    int found = systems.FindIndex(s => s.type == around);
                    if (found < 0)
                        return false;
                    beforeIndex = found;
                    afterIndex = found + 1;
                }

                if (after != null)
                    systems.Insert(afterIndex, new PlayerLoopSystem { type = afterMarker, updateDelegate = after });
                if (before != null)
                    systems.Insert(beforeIndex, new PlayerLoopSystem { type = beforeMarker, updateDelegate = before });

                root.subSystemList[p].subSystemList = systems.ToArray();
                PlayerLoop.SetPlayerLoop(root);
                return true;
            }
            return false;
        }

        public static void Remove(params Type[] markers)
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            if (root.subSystemList == null)
                return;

            bool changed = false;
            for (int p = 0; p < root.subSystemList.Length; p++)
            {
                var subs = root.subSystemList[p].subSystemList;
                if (subs == null)
                    continue;
                var kept = new List<PlayerLoopSystem>(subs.Length);
                foreach (var s in subs)
                {
                    if (Array.IndexOf(markers, s.type) >= 0)
                        changed = true;
                    else
                        kept.Add(s);
                }
                root.subSystemList[p].subSystemList = kept.ToArray();
            }
            if (changed)
                PlayerLoop.SetPlayerLoop(root);
        }
    }
}
