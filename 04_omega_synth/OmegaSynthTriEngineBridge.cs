using System;
using OmegaCommon;

namespace OmegaSynthStudio
{
    public sealed partial class OmegaSynthCockpitForm
    {
        private OmegaTriEngineSharedBus _triBus;
        private double _lastTriBusSec = -1.0;
        private readonly float[] _triBands8 = new float[8];

        private void PublishTriEngineBusFrame(double simSec)
        {
            if (simSec - _lastTriBusSec < 0.015) return;
            _lastTriBusSec = simSec;
            try
            {
                if (_triBus == null) _triBus = new OmegaTriEngineSharedBus(OmegaTriEngineSharedBus.DefaultMapName);
                for (int b = 0; b < 8; b++)
                {
                    int s0 = b * 4;
                    _triBands8[b] = (_spec32[s0] + _spec32[s0 + 1] + _spec32[s0 + 2] + _spec32[s0 + 3]) * 0.25f;
                }
                float bass = (_triBands8[0] + _triBands8[1]) * 0.5f;
                float mid  = (_triBands8[2] + _triBands8[3] + _triBands8[4]) / 3.0f;
                float high = (_triBands8[5] + _triBands8[6] + _triBands8[7]) / 3.0f;
                float peak = Math.Max(_audio.PeakLevelL, _audio.PeakLevelR);
                _triBus.PublishSynthSlice(
                    (float)simSec,
                    bass, mid, high, peak,
                    _triBands8,
                    _audio.ChladniM, _audio.ChladniN, (float)_audio.ArpBpm,
                    18.4f, _audio.LeadVoiceRoot);
            }
            catch { }
        }

        private void DisposeTriEngineBus()
        {
            if (_triBus != null)
            {
                try { _triBus.Dispose(); } catch { }
                _triBus = null;
            }
        }
    }
}