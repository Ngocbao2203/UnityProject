using System;
using System.Collections.Generic;

namespace CGP.Networking.DTOs
{
    [Serializable]
    public class FarmlandItemRef
    {
        public string id;
        public string name; // optional
    }

    [Serializable]
    public class FarmlandCropDto
    {
        public string seedId;
        public int stage;
        public bool isActive;
        public string stageEndsAtUtc;
        public FarmlandItemRef item;
    }

    [Serializable]
    public class FarmlandPlotDto
    {
        public int tileId;
        public string status;      // "Empty" | "Plowed" | "Watered" | "Planted" | "Harvestable"
        public bool watered;
        public List<FarmlandCropDto> farmlandCrops;
    }
}
