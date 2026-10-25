using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Capri
{
    // Weather (off until you turn it on and give a city): open-meteo.com about once an hour.
    partial class Pet
    {
        static HttpClient http;
        public static HttpClient Http
        {
            get
            {
                if (http == null)
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("CapriDesktopCompanion/3.0");
                }
                return http;
            }
        }
        public static readonly string Deg = "°";

        public double? weatherLat, weatherLon; public string weatherPlace = "", weatherForCity = "";
        public int weatherCode = -1; public double weatherTemp, weatherHigh, weatherLow; public int? weatherRain;
        public DateTime? weatherFetched; public string weatherKind = "", weatherReportDay = "", weatherError = "";
        bool weatherPendingReport, weatherPendingChange, weatherBusy; public double nextWeather = 25; string weatherFollowUp = "";

        public static readonly Dictionary<string, string> WeatherReaction = new Dictionary<string, string> { { "clear", "stretch" }, { "cloudy", "sideglance" }, { "fog", "scan" }, { "rain", "grumpy" }, { "snow", "celebrate" }, { "storm", "shiver" }, { "hot", "wilt" }, { "cold", "shiver" } };

        void InitWeather() { }

        public static string WeatherDescription(int code)
        {
            switch (code)
            {
                case 0: return "clear skies"; case 1: return "mostly clear"; case 2: return "partly cloudy"; case 3: return "overcast";
                case 45: case 48: return "fog"; case 51: case 53: case 55: return "drizzle"; case 56: case 57: return "freezing drizzle";
                case 61: return "light rain"; case 63: return "rain"; case 65: return "heavy rain"; case 66: case 67: return "freezing rain";
                case 71: return "light snow"; case 73: return "snow"; case 75: return "heavy snow"; case 77: return "snow grains";
                case 80: case 81: return "rain showers"; case 82: return "violent rain showers"; case 85: case 86: return "snow showers";
                case 95: return "a thunderstorm"; case 96: case 99: return "a thunderstorm with hail";
                default: return "weather";
            }
        }

        // clear | cloudy | fog | rain | snow | storm, or hot / cold when the temperature says more.
        public string WeatherKind(int code, double temp)
        {
            double c = weatherUnit == "F" ? (temp - 32) * 5 / 9 : temp;
            string kind = code <= 1 ? "clear" : code <= 3 ? "cloudy" : (code == 45 || code == 48) ? "fog" :
                ((code >= 51 && code <= 67) || (code >= 80 && code <= 82)) ? "rain" : ((code >= 71 && code <= 77) || code == 85 || code == 86) ? "snow" : code >= 95 ? "storm" : "cloudy";
            if (c >= 30 && (kind == "clear" || kind == "cloudy")) return "hot";
            if (c <= 2 && (kind == "clear" || kind == "cloudy" || kind == "fog")) return "cold";
            return kind;
        }

        static string Round(double n) => Math.Round(n).ToString(CultureInfo.InvariantCulture);

        public void ResetWeather()
        {
            weatherLat = null; weatherLon = null; weatherForCity = ""; weatherCode = -1; weatherKind = ""; weatherFetched = null; weatherError = "";
            nextWeather = Now + 2;
        }

        async void UpdateWeather(double now)
        {
            if (App.Friend || !weatherEnabled || weatherCity.Trim() == "") return;
            if ((weatherPendingReport || weatherPendingChange) && CanReact && state != "catchgame")
            {
                double idle = 0; try { idle = CapriDesktop.IdleSeconds(); } catch { }
                if (idle < 30 && !voiceBusy) ShowWeatherReaction();
            }
            if (weatherFollowUp != "" && state != "weather" && Now - stateStart > 1.5 && Now > messageUntil)
            {
                var kind = weatherFollowUp; weatherFollowUp = "";
                ShowRemark("weather" + kind, true, Round(weatherTemp) + Deg + weatherUnit);
            }
            if (weatherBusy || now < nextWeather) return;
            nextWeather = now + 900; weatherBusy = true;
            try
            {
                if (weatherLat == null || weatherForCity != weatherCity)
                {
                    var parts = weatherCity.Split(new[] { ',' }, 2);
                    string name = parts[0].Trim(), hint = parts.Length > 1 ? parts[1].Trim() : "";
                    var text = await Http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?count=10&language=en&format=json&name=" + Uri.EscapeDataString(name));
                    var results = Json.List(Json.Get(Json.Parse(text), "results"));
                    if (hint != "")
                    {
                        var match = results.Where(r => Json.Str(Json.Get(r, "country")).StartsWith(hint, StringComparison.OrdinalIgnoreCase) || Json.Str(Json.Get(r, "country_code")).Equals(hint, StringComparison.OrdinalIgnoreCase) || Json.Str(Json.Get(r, "admin1")).StartsWith(hint, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (match.Count > 0) results = match;
                    }
                    if (results.Count == 0)
                    {
                        weatherError = "no place called \"" + weatherCity + "\" was found";
                        ShowProblem("Weather: I couldn't find \"" + weatherCity + "\". Try just the city name in Settings > Fun.");
                        nextWeather = double.PositiveInfinity; return;
                    }
                    weatherLat = Json.Num(Json.Get(results[0], "latitude")); weatherLon = Json.Num(Json.Get(results[0], "longitude"));
                    weatherPlace = Json.Str(Json.Get(results[0], "name")); weatherForCity = weatherCity;
                }
                var ci = CultureInfo.InvariantCulture;
                string url = "https://api.open-meteo.com/v1/forecast?latitude=" + weatherLat.Value.ToString(ci) + "&longitude=" + weatherLon.Value.ToString(ci) +
                    "&current=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=1";
                if (weatherUnit == "F") url += "&temperature_unit=fahrenheit";
                var data = Json.Parse(await Http.GetStringAsync(url));
                var current = Json.Get(data, "current"); var daily = Json.Get(data, "daily");
                weatherCode = (int)Json.Num(Json.Get(current, "weather_code")); weatherTemp = Json.Num(Json.Get(current, "temperature_2m"));
                weatherHigh = daily != null ? Json.Num(Json.List(Json.Get(daily, "temperature_2m_max")).FirstOrDefault(), weatherTemp) : weatherTemp;
                weatherLow = daily != null ? Json.Num(Json.List(Json.Get(daily, "temperature_2m_min")).FirstOrDefault(), weatherTemp) : weatherTemp;
                var rain = daily != null ? Json.List(Json.Get(daily, "precipitation_probability_max")).FirstOrDefault() : null;
                weatherRain = rain != null ? (int?)Json.Num(rain) : null;
                weatherFetched = DateTime.Now; weatherError = "";
                var kind = WeatherKind(weatherCode, weatherTemp);
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                if (weatherReportDay != today && DateTime.Now.Hour >= 6) { weatherReportDay = today; weatherPendingReport = true; }
                else if (weatherKind != "" && kind != weatherKind) weatherPendingChange = true;
                weatherKind = kind;
                nextWeather = Now + Math.Max(15.0, tuning.weatherMinutes) * 60;
            }
            catch (Exception e) { weatherError = (e.GetBaseException() ?? e).Message; nextWeather = Now + 900; }
            finally { weatherBusy = false; }
        }

        void ShowWeatherReaction()
        {
            var kind = weatherKind;
            if (kind == "") { weatherPendingReport = false; weatherPendingChange = false; return; }
            string unit = Deg + weatherUnit;
            routine.Clear(); BeginState("weather");
            if (WeatherReaction.ContainsKey(kind)) routine.Enqueue(WeatherReaction[kind]);
            if (weatherPendingReport)
            {
                ShowRemark("weatherreport", true, new object[] { weatherPlace, WeatherDescription(weatherCode), Round(weatherTemp) + unit, Round(weatherHigh) + unit });
                messageUntil = Now + 7;
                weatherFollowUp = kind;
            }
            else ShowRemark("weather" + kind, false, Round(weatherTemp) + unit);
            weatherPendingReport = false; weatherPendingChange = false;
        }

        public void RequestWeatherReport()
        {
            if (weatherEnabled && weatherCity != "") { nextWeather = 0; if (weatherFetched != null) weatherPendingReport = true; }
        }

        public string WeatherSummary()
        {
            if (!weatherEnabled) return "Weather is off (Settings > Fun).";
            if (weatherFetched == null) return "No forecast yet" + (weatherError != "" ? " (" + weatherError + ")" : ".");
            string unit = Deg + weatherUnit;
            var text = weatherPlace + ": " + WeatherDescription(weatherCode) + ", " + Round(weatherTemp) + unit + " (today " + Round(weatherLow) + " to " + Round(weatherHigh) + unit;
            if (weatherRain != null) text += ", " + weatherRain + "% chance of rain";
            return text + "). Checked at " + weatherFetched.Value.ToString("HH:mm") + ".";
        }
    }
}
