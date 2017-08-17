
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WeatherAssignment;

namespace WeatherAssignment
{

    public class Clouds
    {

        [JsonProperty("all")]
        public int All { get; set; }
    }

}
