using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TryCameraEnguCV
{
    public static class AppConstants
    {
        public static double avgGreenDefalt { get; set; } 
        public static double avgRedDefalt { get; set; }
        public static double avgBlueDefalt { get; set; }

        public static double kRedDefault { get; set; }
        public static double kBlueDefault { get; set; }

        public static double wbDefaultOffset { get; set; } = (3421 - 4650) / 1850;
        public static double wbResultFactor { get; set; } = 0;

        public static bool isDefaultWBCounted { get; set; } = false;
        public static bool isAutoWBCounted { get; set; } = true;

    }
}
