using System;
using OmegaCommon;

namespace OmegaPenroseStudio
{
    public sealed partial class OmegaPenroseCockpitForm
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
                int mappedSym = 0;
                if (_symMode == 6) mappedSym = 1;      // 8D E8 Coxeter
                else if (_symMode == 7) mappedSym = 2; // Einstein Hat(1, sqrt(3))
                else if (_symMode == 8) mappedSym = 3; // Chiral Spectre(1, 1)
                float px = (float)(0.28 * Math.Sin(_phasonPhase));
                float py = (float)(0.28 * Math.Cos(_phasonPhase));
                float pz = (float)(0.18 * Math.Sin(_phasonPhase * 0.61803399));
                float scale = Math.Max(0.35f, Math.Min(3.2f, 12.0f / Math.Max(2.0f, _zoomUnits)));
                _triBus.PublishPenroseSlice((float)simSec, _gamma5, px, py, pz, mappedSym, scale);
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