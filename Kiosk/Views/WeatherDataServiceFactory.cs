using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WeatherAssignment.DataClasses;

namespace WeatherAssignment
{

    //Singleton
    public class WeatherDataServiceFactory : IWeatherDataService
    {
        private static WeatherDataServiceFactory instance;

        private WeatherDataServiceFactory() { }

        public static WeatherDataServiceFactory Instance
        {
            get
            {
                if (instance == null)                                   //return new instance if not created yet
                {
                    instance = new WeatherDataServiceFactory();
                }

                return instance;
            }
        }

        public async Task<CityWeather> GetWeatherDataService(Location location)
        {

            HttpClient client = new HttpClient();

            //client.Encoding = System.Text.Encoding.UTF8;

            string url = "http://api.openweathermap.org/data/2.5/weather?q=" + location.city + "&appid=62cf74b0b81efaec9cef03804a15f255";        //url with location & app id

            string json;

            try
            {
                json = await client.GetStringAsync(url);                                          //download json according to url
            }
            catch (Exception e)
            {
                throw new WeatherDataServiceException(e + "Internet Connection failed.");
            }

            JObject jobject = JObject.Parse(json);
            //System.Console.WriteLine(jobject);                                            //all the details from the api
            var obj = JsonConvert.DeserializeObject<CityWeather>(json);
            return obj;                                                                     //return weather data object
        }

        CityWeather IWeatherDataService.GetWeatherDataService(Location location)
        {
            throw new NotImplementedException();
        }
    }
}
