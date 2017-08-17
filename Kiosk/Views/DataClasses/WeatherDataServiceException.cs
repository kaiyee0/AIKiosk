using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeatherAssignment.DataClasses
{
    public class WeatherDataServiceException : Exception
    {
        public WeatherDataServiceException()
            : base() { }

        public WeatherDataServiceException(string message)
            : base(message) { }
    }
}
