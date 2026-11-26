using System;
using OmegaCommon;

namespace OmegaAVStudio
{
    public sealed partial class OmegaAvForm
    {
        private OmegaTriEngineSharedBus _triBus;
        private double _lastTriBusSec = -1.0;

        private void PublishTriEngineBusFrame(double simSec)
        {
            if (simSec - _lastTriBusSec < 0.016) return;
            _lastTriBusSec = simSec;
            try
            {
                if (_triBus == null) _triBus = new OmegaTriEngineSharedBus(OmegaTriEngineSharedBus.DefaultMapName);
                float fluidEnergy = (_fluidMode > 0) ? (float)Math.Min(1.5, 0.45 + 0.55 * _k2) : 0.15f;
                float pk = (_wasapiEnabled && _wasapi.IsCapturing) ? _wasapi.PeakLevel : (float)((_bands[0] + _bands[1]) * 0.5);
                _triBus.PublishAvSlice((float)simSec, fluidEnergy, (float)_k3, _bands, pk, (float)_lastSqnrDb, _lastRootIdx);
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