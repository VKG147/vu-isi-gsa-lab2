using Microsoft.Win32;
using NAudio.Wave;
using ScottPlot.Plottable;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Lab2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        enum TimeUnit
        {
            Milliseconds = 0,
            Seconds = 1,
            Minutes = 2
        }

        private const int MaxAudioFileBytes = 44100 * 60 * 60; // 1 hour of 44100 bitrate

        private TimeUnit _currentTimeUnit = TimeUnit.Minutes;
        private double _segmentTreshold = 0;

        private HLine? _segmentHLine = null;
        private List<VLine> _segmentVLines = new List<VLine>();

        private VLine? _timeMarkerLine = null;
        private IPlottable? _channel1Plot = null;

        private double[] _energyXs;
        private double[] _energyYs;

        private readonly System.Drawing.Color TimerMarkerLineColor = System.Drawing.Color.DarkRed;
        private readonly System.Drawing.Color Channe1PlotColor = System.Drawing.Color.Blue;
        private readonly System.Drawing.Color Channe2PlotColor = System.Drawing.Color.Red;

        public MainWindow()
        {
            InitializeComponent();

            //audioPlot.Plot.XLabel("X (minutes)");
            //audioPlot.Plot.YLabel("Y");

            loadedFileLabel.Content = "";
        }

        private void btnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog()
            {
                Filter = "WAV|*.wav",
                Multiselect = false,
                InitialDirectory = Environment.CurrentDirectory
            };

            if (openFileDialog.ShowDialog() == true)
            {
                audioPlot.Reset();
                energyPlot.Reset();
                nksPlot.Reset();

                _segmentHLine = null;
                _segmentVLines = new List<VLine>();

                channel1Checkbox.IsEnabled = false;
                channel2Checkbox.IsEnabled = false;
                channel1Checkbox.IsChecked = false;
                channel2Checkbox.IsChecked = false;

                string filePath = openFileDialog.FileName;
                string fileName = System.IO.Path.GetFileName(filePath);

                loadedFileLabel.Content = fileName;

                using WaveFileReader reader = new WaveFileReader(filePath);
                if (reader.Length > MaxAudioFileBytes)
                {
                    return;
                }
                if (reader.WaveFormat.Channels > 2)
                {
                    return;
                }

                List<double> channel1Ys = new();
                int maxAudioPlotYValue = (int)Math.Round(Math.Pow(2.0, reader.WaveFormat.BitsPerSample - 1));

                while (reader.Position < reader.Length)
                {
                    float[] sampleFrame = reader.ReadNextSampleFrame();
                    channel1Ys.Add(sampleFrame[0] * maxAudioPlotYValue);

                    //if (sampleFrame.Length > 1)
                    //{
                    //    channel2Ys.Add(sampleFrame[1] * maxAudioPlotYValue);
                    //}
                }

                double totalTimeMultiplier = 0;
                if (reader.TotalTime.TotalSeconds < 1)
                {
                    totalTimeMultiplier = reader.TotalTime.TotalMilliseconds;
                    _currentTimeUnit = TimeUnit.Milliseconds;
                    //audioPlot.Plot.XLabel("X (milliseconds)");
                    //audioPlot.Plot.YLabel("Y");
                } 
                else if (reader.TotalTime.TotalSeconds < 60)
                {
                    totalTimeMultiplier = reader.TotalTime.TotalSeconds;
                    _currentTimeUnit = TimeUnit.Seconds;
                    //audioPlot.Plot.XLabel("X (seconds)");
                    //audioPlot.Plot.YLabel("Y");
                }
                else
                {
                    totalTimeMultiplier = reader.TotalTime.TotalMinutes;
                    _currentTimeUnit = TimeUnit.Minutes;
                    //audioPlot.Plot.XLabel("X (minutes)");
                    //audioPlot.Plot.YLabel("Y");
                }

                double[] channel1Xs = new double[channel1Ys.Count];
                for (int i = 0; i < channel1Xs.Length; i++)
                {
                    channel1Xs[i] = (double)i / channel1Xs.Length * totalTimeMultiplier;
                }
                _channel1Plot = audioPlot.Plot.AddSignalXY(channel1Xs, channel1Ys.ToArray(), color: Channe1PlotColor, label: "channel1");
                channel1Checkbox.IsEnabled = true;
                channel1Checkbox.IsChecked = true;

                audioPlot.Refresh();

                GenerateEnergyPlot(channel1Ys.ToArray(), reader.TotalTime.TotalMilliseconds, _currentTimeUnit);
                GenerateNksPlot(channel1Ys.ToArray(), reader.TotalTime.TotalMilliseconds, _currentTimeUnit);
                if (segmentCheckbox.IsChecked ?? false) 
                    GenerateSegmentLines();
            }
        }

        private void GenerateEnergyPlot(double[] values, double totalMilliseconds, TimeUnit timeUnit)
        {
            int frameCount = (int)(totalMilliseconds / 20); // 20ms
            int frameLen = values.Length / frameCount;
            int frameOverlap = frameLen / 2;

            _energyYs = new double[frameCount];

            for (int frame = 0; frame < frameCount; ++frame)
            {
                double e = 0;
                int offset = 0;
                if (frame != 0) offset = frameLen - frameOverlap;

                int initIndex = frame * frameLen - offset;
                for (int i = initIndex; i < initIndex + frameLen && i < values.Length; ++i)
                {
                    e += values[i] * values[i];
                }

                _energyYs[frame] = e / frameLen;
            }

            double totalTimeMultiplier = ConvertTimeUnit(totalMilliseconds, TimeUnit.Milliseconds, timeUnit);

            _energyXs = new double[_energyYs.Length];
            for (int i = 0; i < _energyXs.Length; i++)
            {
                _energyXs[i] = (double)i / _energyXs.Length * totalTimeMultiplier;
            }

            energyPlot.Plot.AddSignalXY(_energyXs, _energyYs, color: Channe1PlotColor, label: "energy");
            energyPlot.Refresh();
        }

        private void GenerateNksPlot(double[] values, double totalMilliseconds, TimeUnit timeUnit)
        {
            int frameCount = (int)(totalMilliseconds / 20); // 20ms
            int frameLen = values.Length / frameCount;
            int frameOverlap = frameLen / 2;

            double[] nksValues = new double[frameCount];

            for (int frame = 0; frame < frameCount; ++frame)
            {
                double s = 0;
                int offset = 0;
                if (frame != 0) offset = frameLen - frameOverlap;

                int initIndex = frame * frameLen - offset;
                for (int i = initIndex + 1; i < initIndex + frameLen && i < values.Length; ++i)
                {
                    s += Math.Abs((values[i] >= 0 ? 1 : 0) - (values[i-1] >= 0 ? 1 : 0));
                }

                nksValues[frame] = s / (2 * frameLen);
            }

            double totalTimeMultiplier = ConvertTimeUnit(totalMilliseconds, TimeUnit.Milliseconds, timeUnit);

            double[] xs = new double[nksValues.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] = (double)i / xs.Length * totalTimeMultiplier;
            }

            nksPlot.Plot.AddSignalXY(xs, nksValues, color: Channe1PlotColor, label: "NKS");
            nksPlot.Refresh();
        }

        private void txtboxMarkerTime_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtboxMarkerTime.Text))
            {
                RemoveTimeMarkerLine();
                return;
            }

            string markerTimeText = txtboxMarkerTime.Text.Replace('.', ',');
            if (double.TryParse(markerTimeText, out double markerTimeValue))
            {
                UpdateTimeMarkerLine(markerTimeValue);
            }
        }

        private void comboTimeUnit_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtboxMarkerTime.Text))
                return;

            string markerTimeText = txtboxMarkerTime.Text.Replace('.', ',');
            if (double.TryParse(markerTimeText, out double markerTimeValue))
            {
                string previousUnit = (string)(e.RemovedItems[0] as ComboBoxItem ?? throw new Exception()).Content;
                string currentUnit = (string)(e.AddedItems[0] as ComboBoxItem ?? throw new Exception()).Content;

                if (previousUnit != currentUnit)
                {
                    TimeUnit unitFrom = Enum.Parse<TimeUnit>(previousUnit, true);
                    TimeUnit unitTo = Enum.Parse<TimeUnit>(currentUnit, true);
                    markerTimeValue = ConvertTimeUnit(markerTimeValue, unitFrom, unitTo);

                    txtboxMarkerTime.Text = markerTimeValue.ToString();
                }

                UpdateTimeMarkerLine(markerTimeValue);
            }
        }

        private void UpdateTimeMarkerLine(double markedTimeValue)
        {
            RemoveTimeMarkerLine();

            string userSelectedTimeUnit = (string)((ComboBoxItem)comboTimeUnit.SelectedValue).Content;

            TimeUnit selectedUnit = Enum.Parse<TimeUnit>(userSelectedTimeUnit, true);
            double adjustedTime = ConvertTimeUnit(markedTimeValue, selectedUnit, _currentTimeUnit);

            _timeMarkerLine = audioPlot.Plot.AddVerticalLine(adjustedTime, color: TimerMarkerLineColor);

            audioPlot.Refresh();
        }

        private double ConvertTimeUnit(double time, TimeUnit from, TimeUnit to)
        {
            if (from == TimeUnit.Milliseconds && to == TimeUnit.Seconds)
                return TimeSpan.FromMilliseconds(time).TotalSeconds;
            else if (from == TimeUnit.Milliseconds && to == TimeUnit.Minutes)
                return TimeSpan.FromMilliseconds(time).TotalMinutes;
            else if (from == TimeUnit.Seconds && to == TimeUnit.Milliseconds)
                return TimeSpan.FromSeconds(time).TotalMilliseconds;
            else if (from == TimeUnit.Seconds && to == TimeUnit.Minutes)
                return TimeSpan.FromSeconds(time).TotalMinutes;
            else if (from == TimeUnit.Minutes && to == TimeUnit.Milliseconds)
                return TimeSpan.FromMinutes(time).TotalMilliseconds;
            else if (from == TimeUnit.Minutes && to == TimeUnit.Seconds)
                return TimeSpan.FromMinutes(time).TotalSeconds;

            return time;
        }

        private void RemoveTimeMarkerLine()
        {
            if (_timeMarkerLine is not null)
                audioPlot.Plot.Remove(_timeMarkerLine);

            audioPlot.Refresh();
        }

        private void channel1Checkbox_Checked(object sender, RoutedEventArgs e)
        {
            if (_channel1Plot is null) throw new Exception($"Unexpected {nameof(_channel1Plot)} value.");
            _channel1Plot.IsVisible = true;
            audioPlot.Refresh();
        }

        private void channel1Checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_channel1Plot is null) throw new Exception($"Unexpected {nameof(_channel1Plot)} value.");
            _channel1Plot.IsVisible = false;
            audioPlot.Refresh();
        }

        private void GenerateSegmentLines()
        {
            // Remove previous lines
            energyPlot.Plot.Remove(_segmentHLine);
            _segmentHLine = null;

            foreach (var line in _segmentVLines)
                energyPlot.Plot.Remove(line);

            _segmentVLines = new List<VLine>();

            // Add new lines
            _segmentHLine = energyPlot.Plot.AddHorizontalLine(_segmentTreshold, color: System.Drawing.Color.Red);

            for (int i = 1; i < _energyXs.Length - 1; ++i)
            {
                if ((_energyYs[i] >= _segmentTreshold && _energyYs[i - 1] < _segmentTreshold) ||
                    (_energyYs[i - 1] >= _segmentTreshold && _energyYs[i] < _segmentTreshold))
                {
                    VLine line = energyPlot.Plot.AddVerticalLine((_energyXs[i] + _energyXs[i-1])/2, color: System.Drawing.Color.Red);
                    _segmentVLines.Add(line);
                }
            }

            energyPlot.Refresh();
        }

        private void txtboxTreshold_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!double.TryParse(txtboxTreshold.Text, out _segmentTreshold))
                return;
            if (!(segmentCheckbox.IsChecked ?? false))
                return;

            GenerateSegmentLines();
        }

        private void segmentCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_segmentHLine != null)
                _segmentHLine.IsVisible = false;

            foreach (VLine line in _segmentVLines)
            {
                line.IsVisible = false;
            }
            energyPlot.Refresh();
        }

        private void segmentCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            GenerateSegmentLines();
        }
    }
}
