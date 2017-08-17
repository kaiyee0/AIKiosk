using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeatherAssignment
{
    interface IWeatherDataService
    {
        CityWeather GetWeatherDataService(Location location);

    }
}