﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;

namespace alarms
{
    class IOTSimulator
    {
        private static readonly HttpClient _client = new HttpClient();

        // Event Grid
        private static string _eventTopicEndpoint = null;
        private static string _eventTopicResource = null;
        private static string _eventAegSasKey = null;

        // Speed of event publishing, ms between each event
        private static int _eventInterval = 10000;

        // Images    
        private static string _falseAlarmImageURL = null;
        private static string _trueAlarmImageURL = null;

        // Locations for simulated IOT devices
        private static int _numberDevices = 10;

        private static Alarm[] _devices;

        // Status weighting to skew more green vs red and amber
        private static int _statusWeighting = 10;

        // Maximum time for the events to be generated (in minutes)
        // 0 equates to no maximum (run forever)
        private static int _maxRunTime = 10;
        private static DateTime _endTime;

        // Hold boundary conditions for longitude and latitude
        // Don't need to calculate more than once
        private static int _integralMaxLat;
        private static int _fractionalMaxLat;
        private static int _integralMinLat;
        private static int _fractionalMinLat; 
        private static int _integralMaxLong;
        private static int _fractionalMaxLong;  
        private static int _integralMinLong;
        private static int _fractionalMinLong;

        // Longitude and Latitude boundaries within which to create event locations
        // Example rectangle that describes the bulk of England without hitting sea
        // Bottom left 51.010299, -3.114624 (Taunton)
        // Bottom right 51.083686, -0.145569 (Mid Sussex)
        // Top left 53.810382, -3.048706 (Blackpool)
        // Top right 53.745462, -0.346069 (Hull)
        // Use these as default if not supplied in args
        private static decimal _maxLat = 53.810382m;
        private static decimal _minLat = 51.010299m;
        private static decimal _maxLong = -0.145569m;
        private static decimal _minLong = -3.048706m;
        
        // IOTSimulator 
        static void Main(string[] args)
        {
            string usageOutput = "\nUsage\n" +
            "\nRequired environment variables" + 
            "\n------------------------------" + 
            "\n\nAlarmTopic: The Event Grid Topic EndPoint." +
            "\nAlarmResource: The path to the resource in the form: /subscriptions/[your subscription id]/resourceGroups/[your resource group name]/providers/Microsoft.EventGrid/topics/[your EventGrid topic name]." +
            "\nAlarmKey: The Event Grid Topic key." + 
            "\nAlarmFalseImage: The URL to an image that can be used for a false positive event." +
            "\nAlarmTrueImage: The URL to an image that can be used for a positive event." +
            "\n\nOptional environment variables" +
            "\n------------------------------" + 
            "\n\nAlarmInterval: The ms between alarm events, default = 10000." +
            "\nAlarmNumDevices: The number of alarms, default = 10." +
            "\nAlarmMaxLat AlarmMinLat AlarmMaxLong AlarmMinLong - Describes the area within which random cordinates will be created, default = central England." +
            "\nLatitude and Longitude must all be decimal with 6 significant points and all 4 must be provided." +
            "\nAlarmStatusWeight: Must be more than 2, the lower the weighting the proportionally more red status alerts. Default = 10" +
            "\nAlarmMaxRunTime: The maximum number of minutes for the events to be generated, zero for no max. Default = 10";
            
            if (args.Length > 0)
            {
                Console.WriteLine(usageOutput);
                return;
            }
            
            // Required environment variables
            if (Environment.GetEnvironmentVariable("AlarmTopic") != null &&
                Environment.GetEnvironmentVariable("AlarmResource") != null &&
                Environment.GetEnvironmentVariable("AlarmKey") != null &&
                Environment.GetEnvironmentVariable("AlarmFalseImage") != null &&
                Environment.GetEnvironmentVariable("AlarmTrueImage") != null)
            {
                _eventTopicEndpoint = Environment.GetEnvironmentVariable("AlarmTopic");
                _eventTopicResource = Environment.GetEnvironmentVariable("AlarmResource");
                _eventAegSasKey = Environment.GetEnvironmentVariable("AlarmKey");
                _falseAlarmImageURL = Environment.GetEnvironmentVariable("AlarmFalseImage");
                _trueAlarmImageURL = Environment.GetEnvironmentVariable("AlarmTrueImage");
            }
            else
            {
                Console.WriteLine("Missing required environment variable(s).");
                Console.WriteLine(usageOutput);
                return;
            } 

            // Optional environment variables
            try
            {
                // If the interval is supplied, override the default
                if ( Environment.GetEnvironmentVariable("AlarmInterval") != null)
                {
                    int.TryParse(Environment.GetEnvironmentVariable("AlarmInterval"), out _eventInterval);
                }
                
                // If the number of devices is supplied, override the default
                if (Environment.GetEnvironmentVariable("AlarmNumDevices") != null)
                {
                    int.TryParse(Environment.GetEnvironmentVariable("AlarmNumDevices"), out _numberDevices);
                }

                // If ALL the boundary locations are supplied, override the defaults
                if (Environment.GetEnvironmentVariable("AlarmMaxLat") != null && 
                    Environment.GetEnvironmentVariable("AlarmMinLat") != null && 
                    Environment.GetEnvironmentVariable("AlarmMaxLong") != null && 
                    Environment.GetEnvironmentVariable("AlarmMinLong") != null)
                {
                    decimal.TryParse(Environment.GetEnvironmentVariable("AlarmMaxLat"), out _maxLat);
                    decimal.TryParse(Environment.GetEnvironmentVariable("AlarmMinLat"), out _minLat);
                    decimal.TryParse(Environment.GetEnvironmentVariable("AlarmMaxLong"), out _maxLong);
                    decimal.TryParse(Environment.GetEnvironmentVariable("AlarmMinLong"), out _minLong);
                }

                // If the status weighting is supplied, override the default
                if (Environment.GetEnvironmentVariable("AlarmStatusWeight") != null)
                {
                    int.TryParse(Environment.GetEnvironmentVariable("AlarmStatusWeight"), out _statusWeighting);
                }

                // If the maximimum time is supplied, override the default
                if (Environment.GetEnvironmentVariable("AlarmMaxRunTime") != null)
                {
                    int.TryParse(Environment.GetEnvironmentVariable("AlarmMaxRunTime"), out _maxRunTime);
                }
            }
            catch (Exception e) 
            {
                Console.WriteLine("Environment variable error: " + e.Message);
                Console.WriteLine(usageOutput);
                return;
            }
            
            Console.Write("Alarm settings: " + "\n Topic EndPoint: " + _eventTopicEndpoint + 
            "\n Topic Key (last chars): " + _eventAegSasKey.Substring(_eventAegSasKey.Length - 4, 4) + "\n Topic Resource: " + _eventTopicResource + 
            "\n False Image: " + _falseAlarmImageURL + "\n True Image: " + _trueAlarmImageURL);
            
            Console.Write("\nAlarms will be sent every " + _eventInterval + " ms.");
            Console.Write("\nThe simulator will stop after " + _maxRunTime + " mins.\n");

            SetLocationBoundaries(_maxLat, _minLat, _maxLong, _minLong);
            SetDevices();
            _endTime = DateTime.Now.AddMinutes(_maxRunTime);
            
            SimulateAlarms().Wait();
        }

        private static void SetDevices()
        {
            // Create a fixed set of devices
            _devices = new Alarm[_numberDevices];

            // Add location into each Alarm
            for (int i = 0; i < _devices.Length; i++)
            {
                _devices[i] = new Alarm();
                _devices[i].DeviceId = i;
                var location = GetAlarmLocation();
                _devices[i].Longitude = location.longitude;
                _devices[i].Latitude = location.latitude;
                _devices[i].Name = "Alarm " + i;
            }
        }

        private static async Task SimulateAlarms()
        {
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client.DefaultRequestHeaders.Add("aeg-sas-key", _eventAegSasKey);

            while(true)
            {
                try
                {    
                    // For each device, send a new alarm, keeping the location static
                    for (int i = 0; i < _devices.Length; i++)
                    {
                        _devices[i].Status = GetAlarmStatus();
                        _devices[i].Image = GetAlarmImage();
                        _devices[i].Text = _devices[i].Status + " alert image: " + _devices[i].Image;
                        
                        // Create a new event
                        AlarmEvent alarmEvent = new AlarmEvent {
                            topic = _eventTopicResource,
                            subject = "Alarm", 
                            id = Guid.NewGuid().ToString(),
                            eventType = "recordInserted", 
                            eventTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFK"),
                            data = _devices[i] };

                        // Event Grid data is an array with one element
                        AlarmEvent[] alarmEvents = { alarmEvent };

                        // Post the data
                        HttpResponseMessage response = await _client.PostAsync(_eventTopicEndpoint, new JsonContent(alarmEvents));

                        if (response.IsSuccessStatusCode)
                        {
                            Console.WriteLine("\n Device " + _devices[i].DeviceId
                            + ". Status: " + _devices[i].Status + ". Longitude: " 
                            + _devices[i].Longitude + ". Latitude: " + _devices[i].Latitude
                            + ". Image: " + _devices[i].Image);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("\nError sending alarm:" + e.Message);
                }
                
                // Exit if max time reached
                if (IsMaxTime())
                {
                    Console.WriteLine("Maximum time reached (" + _maxRunTime + " mins), simulator stopping.");
                    Environment.Exit(1);
                }
                // Pause specified interval before the next batch of alarms
                Thread.Sleep(_eventInterval);
            }
        }

        private static string GetAlarmStatus()
        {
            // Return (pseudo) random red or blue
            // blue as the default Azure Maps pins includes blue but not green
            string alarmStatus = "blue";
            Random random = new Random(Guid.NewGuid().GetHashCode());
            
            // Simplistic weighting to make the majority green
            // e.g. if _statusWeighting is 10 then 0 = red, 1-9 = blue
            int value = random.Next(_statusWeighting);

            switch (value)
            {
                case 0:
                    alarmStatus = "red";
                    break;
                default:
                    alarmStatus = "blue";
                    break;
            }

            return alarmStatus;
        }

        private static (decimal longitude, decimal latitude) GetAlarmLocation()
        {
            Random latRandom = new Random(Guid.NewGuid().GetHashCode());
            int latIntegral = latRandom.Next(_integralMinLat, _integralMaxLat + 1);
            int latFractional = latRandom.Next(_fractionalMinLat, _fractionalMaxLat + 1);
            decimal latitude = latIntegral + (latFractional / 1000000m);

            Random longRandom = new Random(Guid.NewGuid().GetHashCode());
            int longIntegral = longRandom.Next(_integralMinLong, _integralMaxLong + 1);
            int longFractional = latRandom.Next(_fractionalMinLong, _fractionalMaxLong + 1);
            decimal longitude = longIntegral + (longFractional / 1000000m);

            return (longitude, latitude);
        }

        private static string GetAlarmImage()
        {
            // Return either the good (e.g. cat) or bad (e.g. gang) image
            string alarmImage = null;
            Random random = new Random(Guid.NewGuid().GetHashCode());
            
            // Assumed 50/50 weighting?
            int value = random.Next(2);

            if (value == 0)
            {
                alarmImage = _trueAlarmImageURL;
            }
            else
            {
                alarmImage = _falseAlarmImageURL;
            }
            
            return alarmImage;
        }

        private static void SetLocationBoundaries(decimal maxLat, decimal minLat, decimal maxLong, decimal minLong)
        {
            // Break the coordinates into integral and fractional components
            // So that each part can be randomly created within the right boundaries
            _integralMaxLat = (int) maxLat;
            decimal decFractionalMaxLat = maxLat - _integralMaxLat;
            _fractionalMaxLat = (int) (decFractionalMaxLat * GetMultiplyer(decFractionalMaxLat));
            
            _integralMinLat = (int) minLat;
            decimal decFractionalMinLat = minLat - _integralMinLat;
            _fractionalMinLat = (int) (decFractionalMinLat * GetMultiplyer(decFractionalMinLat));
            
            _integralMaxLong = (int)maxLong;
            decimal decFractionalMaxLong = maxLong - _integralMaxLong;
            _fractionalMaxLong = (int) (decFractionalMaxLong * GetMultiplyer(decFractionalMaxLong));
            
            _integralMinLong = (int)minLong;
            decimal decFractionalMinLong = minLong - _integralMinLong;
            _fractionalMinLong = (int) (decFractionalMinLong * GetMultiplyer(decFractionalMinLong)); 

            FlipIfNegative(ref _fractionalMaxLong, ref _fractionalMinLong);
            FlipIfNegative(ref _fractionalMaxLat, ref _fractionalMinLat); 
        }

        private static int GetMultiplyer(decimal value)
        {
            int factor;
            
            switch (value.ToString().Length)
            {
                case 1:
                    factor = 10;
                    break; 
                case 2:
                    factor = 100;
                    break; 
                case 3:
                    factor = 1000;
                    break;  
                case 4:
                    factor = 10000;
                    break;
                case 5:
                    factor = 100000;
                    break;
                default:
                    factor = 1000000;
                    break;
            }

            return factor;
        }

        private static void FlipIfNegative(ref int max, ref int min)
        {
            // Deal with negative Longitudes and Latitudes, 
            // so that when getting random number the min and max work correctly
            if (max < 0 && min < 0)
            {
                // Swap them
                int tmpMax = max;
                int tmpMin = min;

                max = tmpMin;
                min = tmpMax;
            } 
        }

        private static bool IsMaxTime()
        {
            bool stop = false;

            // If it's zero, never time out
            if (_maxRunTime == 0)
            {
                stop = false;
            }
            else if(DateTime.Compare(DateTime.Now, _endTime) > 0)
            {
                // If compare is not zero then now is later than the end time
                stop = true;
            }
            
            return stop;
        }
    }
}
