﻿using NAudio.Wave.SampleProviders;

namespace Proggy.Core
{
    public class ClickSettings
    {
        public SignalGeneratorType WaveType { get; set; }
        public double AccentClickFreq { get; set; }
        public double ClickFreq { get; set; }
        public byte PrecountBarBeats { get; set; }
        public byte PrecountBarNoteLength { get; set; }
    }
}
