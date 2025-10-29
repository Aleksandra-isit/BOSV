using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TryCameraEnguCV
{
    public static class AppConstants
    {
        public static bool AutoWBRequested { get; set; } = false; // запрос пересчёта
        public static double kRedAuto { get; set; } = 1.0;
        public static double kBlueAuto { get; set; } = 1.0;
        public static bool AutoWBActive { get; set; } = false;   // активен ли авто-баланс
    }
}
