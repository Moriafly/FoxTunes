﻿using FoxTunes.Interfaces;
using System;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FoxTunes
{
    public class PeakRenderer : VisualizationBase
    {
        public const int DB_MIN = -90;

        public const int DB_MAX = 0;

        public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
            "Orientation",
            typeof(Orientation),
            typeof(PeakRenderer),
            new FrameworkPropertyMetadata(Orientation.Horizontal, new PropertyChangedCallback(OnOrientationChanged))
        );

        public static Orientation GetOrientation(PeakRenderer source)
        {
            return (Orientation)source.GetValue(OrientationProperty);
        }

        public static void SetOrientation(PeakRenderer source, Orientation value)
        {
            source.SetValue(OrientationProperty, value);
        }

        public static void OnOrientationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var renderer = sender as PeakRenderer;
            if (renderer == null)
            {
                return;
            }
            renderer.OnOrientationChanged();
        }

        public PeakRendererData RendererData { get; private set; }

        public BooleanConfigurationElement ShowPeaks { get; private set; }

        public BooleanConfigurationElement ShowRms { get; private set; }

        public BooleanConfigurationElement Smooth { get; private set; }

        public IntegerConfigurationElement SmoothingFactor { get; private set; }

        public IntegerConfigurationElement HoldInterval { get; private set; }

        public Orientation Orientation
        {
            get
            {
                return (Orientation)this.GetValue(OrientationProperty);
            }
            set
            {
                this.SetValue(OrientationProperty, value);
            }
        }

        protected virtual void OnOrientationChanged()
        {
            var data = this.RendererData;
            if (data != null)
            {
                data.Orientation = this.Orientation;
            }
            if (this.OrientationChanged != null)
            {
                this.OrientationChanged(this, EventArgs.Empty);
            }
            this.OnPropertyChanged("Orientation");
        }

        public event EventHandler OrientationChanged;

        public override void InitializeComponent(ICore core)
        {
            base.InitializeComponent(core);
            this.ShowPeaks = this.Configuration.GetElement<BooleanConfigurationElement>(
                SpectrumBehaviourConfiguration.SECTION,
                SpectrumBehaviourConfiguration.PEAKS_ELEMENT
             );
            this.ShowRms = this.Configuration.GetElement<BooleanConfigurationElement>(
                SpectrumBehaviourConfiguration.SECTION,
                SpectrumBehaviourConfiguration.RMS_ELEMENT
            );
            this.Smooth = this.Configuration.GetElement<BooleanConfigurationElement>(
               SpectrumBehaviourConfiguration.SECTION,
               SpectrumBehaviourConfiguration.SMOOTH_ELEMENT
            );
            this.SmoothingFactor = this.Configuration.GetElement<IntegerConfigurationElement>(
               SpectrumBehaviourConfiguration.SECTION,
               SpectrumBehaviourConfiguration.SMOOTH_FACTOR_ELEMENT
            );
            this.HoldInterval = this.Configuration.GetElement<IntegerConfigurationElement>(
               SpectrumBehaviourConfiguration.SECTION,
               SpectrumBehaviourConfiguration.HOLD_ELEMENT
            );
            this.Configuration.GetElement<IntegerConfigurationElement>(
               SpectrumBehaviourConfiguration.SECTION,
               SpectrumBehaviourConfiguration.INTERVAL_ELEMENT
            ).ConnectValue(value => this.UpdateInterval = value);
            this.ShowPeaks.ValueChanged += this.OnValueChanged;
            this.ShowRms.ValueChanged += this.OnValueChanged;
            this.Smooth.ValueChanged += this.OnValueChanged;
            this.SmoothingFactor.ValueChanged += this.OnValueChanged;
            this.HoldInterval.ValueChanged += this.OnValueChanged;
#if NET40
            var task = TaskEx.Run(async () =>
#else
            var task = Task.Run(async () =>
#endif
            {
                await this.CreateBitmap().ConfigureAwait(false);
                if (PlaybackStateNotifier.IsPlaying)
                {
                    this.Start();
                }
            });
        }

        protected virtual void OnValueChanged(object sender, EventArgs e)
        {
            var task = this.RefreshBitmap();
        }

        protected override void CreateViewBox()
        {
            this.RendererData = Create(
                this,
                this.Bitmap.PixelWidth,
                this.Bitmap.PixelHeight,
                this.Orientation
            );
            this.Viewbox = new Rect(0, 0, this.RendererData.Width, this.RendererData.Height);
        }

        protected virtual Task RefreshBitmap()
        {
            return Windows.Invoke(() =>
            {
                this.RendererData = Create(
                    this,
                    this.Bitmap.PixelWidth,
                    this.Bitmap.PixelHeight,
                    this.Orientation
                );
            });
        }

        protected virtual async Task Render()
        {
            const byte SHADE = 30;

            var bitmap = default(WriteableBitmap);
            var success = default(bool);
            var valueRenderInfo = default(BitmapHelper.RenderInfo);
            var rmsRenderInfo = default(BitmapHelper.RenderInfo);

            await Windows.Invoke(() =>
            {
                bitmap = this.Bitmap;
                success = bitmap.TryLock(LockTimeout);
                if (!success)
                {
                    return;
                }
                if (this.ShowRms.Value)
                {
                    var colors = this.Color.ToPair(SHADE);
                    valueRenderInfo = BitmapHelper.CreateRenderInfo(bitmap, colors[0]);
                    rmsRenderInfo = BitmapHelper.CreateRenderInfo(bitmap, colors[1]);
                }
                else
                {
                    valueRenderInfo = BitmapHelper.CreateRenderInfo(bitmap, this.Color);
                }
            }).ConfigureAwait(false);

            if (!success)
            {
                //Failed to establish lock.
                this.Start();
                return;
            }

            Render(valueRenderInfo, rmsRenderInfo, this.RendererData);

            await Windows.Invoke(() =>
            {
                bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                bitmap.Unlock();
                this.Start();
            }).ConfigureAwait(false);
        }

        protected override void OnElapsed(object sender, ElapsedEventArgs e)
        {
            var data = this.RendererData;
            if (data == null)
            {
                return;
            }
            try
            {
                if (!data.Update())
                {
                    data.Clear();
                }
                UpdateValues(data);
                if (this.Smooth.Value)
                {
                    UpdateElementsSmooth(data);
                }
                else
                {
                    UpdateElementsFast(data);
                }
                if (this.ShowPeaks.Value)
                {
                    var duration = Convert.ToInt32(
                        Math.Min(
                            (DateTime.UtcNow - data.LastUpdated).TotalMilliseconds,
                            this.UpdateInterval * 100
                        )
                    );
                    UpdateElementsSmooth(data.ValueElements, data.PeakElements, data.Holds, data.Width, data.Height, this.HoldInterval.Value, duration, data.Orientation);
                }

                data.LastUpdated = DateTime.UtcNow;

                var task = this.Render();
            }
            catch (Exception exception)
            {
                Logger.Write(this.GetType(), LogLevel.Warn, "Failed to update spectrum data, disabling: {0}", exception.Message);
            }
        }

        protected override Freezable CreateInstanceCore()
        {
            return new PeakRenderer();
        }

        protected override void OnDisposing()
        {
            if (this.ScalingFactor != null)
            {
                this.ScalingFactor.ValueChanged -= this.OnValueChanged;
            }
            if (this.ShowPeaks != null)
            {
                this.ShowPeaks.ValueChanged -= this.OnValueChanged;
            }
            if (this.ShowRms != null)
            {
                this.ShowRms.ValueChanged -= this.OnValueChanged;
            }
            if (this.Smooth != null)
            {
                this.Smooth.ValueChanged -= this.OnValueChanged;
            }
            if (this.SmoothingFactor != null)
            {
                this.SmoothingFactor.ValueChanged -= this.OnValueChanged;
            }
            if (this.HoldInterval != null)
            {
                this.HoldInterval.ValueChanged -= this.OnValueChanged;
            }
        }

        private static void Render(BitmapHelper.RenderInfo valueRenderInfo, BitmapHelper.RenderInfo rmsRenderInfo, PeakRendererData rendererData)
        {
            var valueElements = rendererData.ValueElements;
            var rmsElements = rendererData.RmsElements;
            var peakElements = rendererData.PeakElements;
            var orientation = rendererData.Orientation;

            BitmapHelper.Clear(valueRenderInfo);

            if (valueElements != null)
            {
                for (var a = 0; a < valueElements.Length; a++)
                {
                    BitmapHelper.DrawRectangle(
                        valueRenderInfo,
                        valueElements[a].X,
                        valueElements[a].Y,
                        valueElements[a].Width,
                        valueElements[a].Height
                    );
                    if (rmsElements != null)
                    {
                        BitmapHelper.DrawRectangle(
                            rmsRenderInfo,
                            rmsElements[a].X,
                            rmsElements[a].Y,
                            rmsElements[a].Width,
                            rmsElements[a].Height
                        );
                    }
                    if (peakElements != null)
                    {
                        if (orientation == Orientation.Horizontal)
                        {
                            if (peakElements[a].X <= valueElements[a].Width)
                            {
                                continue;
                            }
                        }
                        else if (orientation == Orientation.Vertical)
                        {
                            if (peakElements[a].Y >= valueElements[a].Y)
                            {
                                continue;
                            }
                        }
                        BitmapHelper.DrawRectangle(
                            valueRenderInfo,
                            peakElements[a].X,
                            peakElements[a].Y,
                            peakElements[a].Width,
                            peakElements[a].Height
                        );
                    }
                }
            }
        }

        private static void UpdateValues(PeakRendererData data)
        {
            switch (data.Format)
            {
                case OutputStreamFormat.Short:
                    UpdateValues(data.Samples16, data.Samples32, data.SampleCount);
                    break;
            }
            UpdateValues(data.Samples32, data.Values, data.Rms, data.Channels, data.SampleCount);
        }

        private static void UpdateValues(short[] samples16, float[] samples32, int count)
        {
            for (var a = 0; a < count; a++)
            {
                samples32[a] = samples16[a] / short.MaxValue;
            }
        }

        private static void UpdateValues(float[] samples, float[] values, float[] rms, int channels, int count)
        {
            var doRms = rms != null;
            for (var channel = 0; channel < channels; channel++)
            {
                values[channel] = 0;
                if (doRms)
                {
                    rms[channel] = 0;
                }
            }

            if (count == 0)
            {
                return;
            }

            for (var position = 0; position < count; position += channels)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var value = samples[position + channel];
                    values[channel] = Math.Max(value, values[channel]);
                    if (doRms)
                    {
                        rms[channel] += value * value;
                    }
                }
            }

            for (var channel = 0; channel < channels; channel++)
            {
                values[channel] = ToDecibelFixed(values[channel]);
            }

            if (doRms)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    rms[channel] = ToDecibelFixed(Convert.ToSingle(Math.Sqrt(rms[channel] / (count / channels))));
                }
            }
        }

        private static void UpdateElementsFast(PeakRendererData data)
        {
            UpdateElementsFast(data.Values, data.ValueElements, data.Width, data.Height, data.Orientation);
            if (data.Rms != null && data.RmsElements != null)
            {
                UpdateElementsFast(data.Rms, data.RmsElements, data.Width, data.Height, data.Orientation);
            }
        }

        private static void UpdateElementsSmooth(PeakRendererData data)
        {
            UpdateElementsSmooth(data.Values, data.ValueElements, data.Width, data.Height, data.Renderer.SmoothingFactor.Value, data.Orientation);
            if (data.Rms != null && data.RmsElements != null)
            {
                UpdateElementsSmooth(data.Rms, data.RmsElements, data.Width, data.Height, data.Renderer.SmoothingFactor.Value, data.Orientation);
            }
        }

        public static PeakRendererData Create(PeakRenderer renderer, int width, int height, Orientation orientation)
        {
            var data = new PeakRendererData()
            {
                Renderer = renderer,
                Width = width,
                Height = height,
                Orientation = orientation
            };
            return data;
        }

        public class PeakRendererData
        {
            public PeakRenderer Renderer;

            public int Width;

            public int Height;

            public Orientation Orientation;

            public int Rate;

            public int Channels;

            public OutputStreamFormat Format;

            public short[] Samples16;

            public float[] Samples32;

            public int SampleCount;

            public float[] Values;

            public float[] Rms;

            public Int32Rect[] ValueElements;

            public Int32Rect[] RmsElements;

            public Int32Rect[] PeakElements;

            public int[] Holds;

            public DateTime LastUpdated;

            public bool Update()
            {
                var rate = default(int);
                var channels = default(int);
                var format = default(OutputStreamFormat);
                if (!this.Renderer.Output.GetFormat(out rate, out channels, out format))
                {
                    return false;
                }
                this.Update(rate, channels, format);
                switch (this.Format)
                {
                    case OutputStreamFormat.Short:
                        this.SampleCount = this.Renderer.Output.GetData(this.Samples16);
                        break;
                    case OutputStreamFormat.Float:
                        this.SampleCount = this.Renderer.Output.GetData(this.Samples32);
                        break;
                }
                return this.SampleCount > 0;
            }

            private void Update(int rate, int channels, OutputStreamFormat format)
            {
                if (this.Rate == rate && this.Channels == channels && this.Format == format)
                {
                    return;
                }

                this.Rate = rate;
                this.Channels = channels;
                this.Format = format;

                if (format == OutputStreamFormat.Short)
                {
                    this.Samples16 = this.Renderer.Output.GetBuffer<short>(TimeSpan.FromMilliseconds(this.Renderer.UpdateInterval));
                    this.Samples32 = new float[this.Samples16.Length];
                }
                else if (format == OutputStreamFormat.Float)
                {
                    this.Samples32 = this.Renderer.Output.GetBuffer<float>(TimeSpan.FromMilliseconds(this.Renderer.UpdateInterval));
                }

                this.Values = new float[channels];
                this.ValueElements = new Int32Rect[channels];

                if (this.Renderer.ShowRms.Value)
                {
                    this.Rms = new float[channels];
                    this.RmsElements = new Int32Rect[channels];
                }
                if (this.Renderer.ShowPeaks.Value)
                {
                    this.PeakElements = new Int32Rect[channels];
                    this.Holds = new int[channels];
                }
            }

            public void Clear()
            {
                if (this.Samples16 != null)
                {
                    Array.Clear(this.Samples16, 0, this.Samples16.Length);
                }
                Array.Clear(this.Samples32, 0, this.Samples32.Length);
                this.SampleCount = 0;
            }
        }
    }
}