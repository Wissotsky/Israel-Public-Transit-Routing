// See https://aka.ms/new-console-template for more information
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;

Console.WriteLine("Starting...");

Console.OutputEncoding = new UTF8Encoding(); // Fix hebrew rendering even though well probably move to english

// Set up default start and end location
string START_LOCATION = "Jerusalem";
string END_LOCATION = "Tami";

// Parse arguments to locations
if(args.Length >= 1)
{
    START_LOCATION = args[0];
    END_LOCATION = args[1];
}

Console.WriteLine($"Start Location: '{START_LOCATION}' End Location: '{END_LOCATION}'");


const int STOPS_COUNT = 51000; // The highest stop id seems to be at 51k
const int ROUTES_COUNT = 40000; // Highest route Id is at about 40k
const int SERVICES_COUNT = 11000000;
int START_STOP_ID = 21271; // code 54135
int END_STOP_ID = 13499; //code 25380

// HTTP client shenanigans, very scuffed.
// Absolute dumbest way to geocode
HttpClient httpClient = new HttpClient();

HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get,$"https://nominatim.openstreetmap.org/search?q=Bus%20Stop%20near%20{START_LOCATION}&format=geocodejson&countrycodes=il,ps&limit=1&extratags=1");
httpRequestMessage.Headers.UserAgent.TryParseAdd("github.com/wissotsky");

HttpResponseMessage responseMessage = httpClient.Send(httpRequestMessage);
//Console.WriteLine(responseMessage);
string jsonResponse = responseMessage.Content.ReadAsStringAsync().Result;
//Console.WriteLine(jsonResponse);

// Parsing json with a regex (¬_¬ )
string regexBadPattern = @"""name"":""(.+?)""";
string regexCoordPattern = @"coordinates"": \[(.+?)\]";
Match match = Regex.Match(jsonResponse,regexCoordPattern);
string startStopLonLatString = $"{match.Groups[1]}"; // Force it to a string
float startStopLon = float.Parse(startStopLonLatString.Split(',')[0]);
float startStopLat = float.Parse(startStopLonLatString.Split(',')[1]);
(float longitude,float latitude) startStopLonLat = (startStopLon,startStopLat); 
Console.WriteLine($"[GEOCODING] Start bus station: {startStopLonLatString}");

//
// Geocode End Location
//

HttpRequestMessage httpRequestMessage2 = new HttpRequestMessage(HttpMethod.Get,$"https://nominatim.openstreetmap.org/search?q=Bus%20Stop%20near%20{END_LOCATION}&format=geocodejson&countrycodes=il,ps&limit=1&extratags=1");
httpRequestMessage2.Headers.UserAgent.TryParseAdd("github.com/wissotsky");

HttpResponseMessage responseMessage2 = httpClient.Send(httpRequestMessage2);
//Console.WriteLine(responseMessage2);
string jsonResponse2 = responseMessage2.Content.ReadAsStringAsync().Result;
//Console.WriteLine(jsonResponse2);

Match match2 = Regex.Match(jsonResponse2,regexCoordPattern);
string endStopLonLatString = $"{match2.Groups[1]}"; // Force it to a string
float endStopLon = float.Parse(endStopLonLatString.Split(',')[0]);
float endStopLat = float.Parse(endStopLonLatString.Split(',')[1]);
(float longitude,float latitude) endStopLonLat = (endStopLon,endStopLat); 
Console.WriteLine($"[GEOCODING] End bus station: {endStopLonLatString}");

// List that stores strings which we failed to translate to avoid repeated error messages
HashSet<string> untranslatableStrings = new HashSet<string>();

// We use sparse arrays for these because the indexed lookup afterwards is very fast
int[] arrivalTimestamp = new int[STOPS_COUNT]; 
(string tripId,int depStop,int arrStop,int depTime,int arrTime)[] inConnection = new (string tripId,int depStop,int arrStop,int depTime,int arrTime)[STOPS_COUNT];

// initialize stations
for (int i = 0; i < STOPS_COUNT; i++)
{
    arrivalTimestamp[i] = int.MaxValue;
    inConnection[i] = ("0",0,0,0,0);
}

// set timestamp on our start stop
//arrivalTimestamp[START_STOP_ID] = ParseTime("09:00:00");

// Load translations.txt
Dictionary<string,string> translationsTable = new Dictionary<string, string>();
//Dictionary<string,List<string>> wordTranslationsTable = new Dictionary<string, List<string>>();

using StreamReader translationsReader = new(Path.Combine("GtfsData", "translations.txt"));

string translationEntry;
while ((translationEntry = translationsReader.ReadLine()) != null)
{
    // hebrew_string,lang,lang_string
    string[] splitTranslation = translationEntry.Split(','); // Ideally we would be using a proper csv parsing library, but I want to keep it as dumb and simple as possible
    if (splitTranslation[1] == "EN")
    {
        // write to direct translations table
        string hebrewString = splitTranslation[0].Replace("''","\""); // Make sure gershayim are consistent
        string langString = splitTranslation[2];
        translationsTable.TryAdd(hebrewString,langString);

    }
}

Console.WriteLine("Translations Loading Done!");

// Load stops.txt
int[] stopCodes = new int[STOPS_COUNT]; 
string[] stopNames = new string[STOPS_COUNT]; 
string[] stopNamesEn = new string[STOPS_COUNT]; 
(float longitude, float latitude)[] stopLocations = new (float  longitude, float latitude)[STOPS_COUNT];

using StreamReader stopsReader = new(Path.Combine("GtfsData", "stops.txt"));

string stopEntry;
while ((stopEntry = stopsReader.ReadLine()) != null)
{
    string[] splitEntry = stopEntry.Split(',');
    if (splitEntry[0] == "stop_id") { continue; }

    float stopLat = float.Parse(splitEntry[4]);
    float stopLon = float.Parse(splitEntry[5]);
    int stopId = int.Parse(splitEntry[0]);
    int stopCode = int.Parse(splitEntry[1]);
    string stopName = splitEntry[2].Replace("''","\""); // Make sure gershayim are consistent;

    string stopNameEn;
    if (!translationsTable.TryGetValue(stopName,out stopNameEn))
    {
        // if we cant translate the stop name
        if (!untranslatableStrings.Contains(stopName))
        {   
            untranslatableStrings.Add(stopName);
            //Console.WriteLine($"[TRANSLATION ERROR] {stopName} not found");
        }
        stopNameEn = stopName;
    }

    stopCodes[stopId] = stopCode;
    stopNames[stopId] = stopName;
    stopNamesEn[stopId] = stopNameEn;
    stopLocations[stopId] = (stopLon,stopLat);

}

Console.WriteLine("Stops Loading Done!");

// Load trips.txt
Dictionary<string,int> tripId2RouteId = new Dictionary<string,int>();
Dictionary<string,int> tripId2ServiceId = new Dictionary<string,int>();
Dictionary<string,string> tripId2TripHeadsign = new Dictionary<string, string>();

using StreamReader tripsReader = new(Path.Combine("GtfsData", "trips.txt"));

string tripEntry;
while ((tripEntry = tripsReader.ReadLine()) != null)
{
    string[] splitEntry = tripEntry.Split(',');
    if (splitEntry[0] == "route_id") { continue; }

    int routeId = int.Parse(splitEntry[0]);
    int serviceId = int.Parse(splitEntry[1]);
    string tripId = splitEntry[2];
    string[] tripHeadsign = splitEntry[3].Split('_');

    List<string> tripHeadsignEn = new List<string>();
    foreach (var tripHeadsignPart in tripHeadsign)
    {
        string tripHeadsignPartEn;
        if (!translationsTable.TryGetValue(tripHeadsignPart,out tripHeadsignPartEn))
        {
            // if we cant translate the stop name
            if (!untranslatableStrings.Contains(tripHeadsignPart))
            {
                // Avoid some of the log duplication
                untranslatableStrings.Add(tripHeadsignPart);
                //Console.WriteLine($"[TRANSLATION ERROR] {tripHeadsignPart} not found");
            }
            tripHeadsignPartEn = tripHeadsignPart;
        }
        tripHeadsignEn.Add(tripHeadsignPartEn);
    }

    tripId2RouteId.Add(tripId,routeId);
    tripId2ServiceId.Add(tripId,serviceId);
    tripId2TripHeadsign.Add(tripId,string.Join('-',tripHeadsignEn));
}

Console.WriteLine("Trips Loading Done!");

// Load routes.txt
// This is basically a sparse array for quick lookups // TODO: Considering the lookups only happen when we print out the legs it might not be worth keeping in memory all the time.
string[] routeShortNames = new string[ROUTES_COUNT]; 

using StreamReader routesReader = new(Path.Combine("GtfsData", "routes.txt"));

string routeEntry;
while ((routeEntry = routesReader.ReadLine()) != null)
{
    string[] splitEntry = routeEntry.Split(',');
    if (splitEntry[0] == "route_id") { continue; }

    int routeId = int.Parse(splitEntry[0]);
    string routeShortName = splitEntry[2];

    routeShortNames[routeId] = routeShortName;
}

Console.WriteLine("Routes Loading Done!");

// Load calendar.txt
// The days of the week coulda just been a uint8
(bool sunday,bool monday,bool tuesday,bool wednesday,bool thursday,bool friday,bool saturday,DateTime startTime, DateTime endTime)[] calendar = new (bool sunday,bool monday,bool tuesday,bool wednesday,bool thursday,bool friday,bool saturday,DateTime startTime, DateTime endTime)[SERVICES_COUNT];

using StreamReader calendarReader = new(Path.Combine("GtfsData", "calendar.txt"));

string calendarEntry;
while ((calendarEntry = calendarReader.ReadLine()) != null)
{
    string[] splitEntry = calendarEntry.Split(',');
    if (splitEntry[0] == "service_id") { continue; }
    bool sunday = (splitEntry[1] == "1");
    bool monday = (splitEntry[2] == "1");
    bool tuesday = (splitEntry[3] == "1");
    bool wednesday = (splitEntry[4] == "1");
    bool thursday = (splitEntry[5] == "1");
    bool friday = (splitEntry[6] == "1");
    bool saturday = (splitEntry[7] == "1");
    DateTime startTime = DateTime.ParseExact(splitEntry[8], "yyyyMMdd", null); // I hope its in the right timezone
    DateTime endTime = DateTime.ParseExact(splitEntry[9], "yyyyMMdd", null);

    // ServiceIds start from 1
    calendar[int.Parse(splitEntry[0])+1] = (sunday, monday, tuesday, wednesday, thursday, friday, saturday, startTime, endTime);
}

string previousEntry = "0,00:00:00,00:00:01,0"; // I need to write a parser that atleast pretends to be robust
var (prevTripId,prevArrivalTime,prevDepartureTime,prevStopId) = ParseEntry(previousEntry);

// Get stop ids from geocode results
// We assume the earth is a flat plane in the wsg84 projection because I dont remember earths radius
// Also we just use squared euclidian distance
int startId = -1;
int endId = -1;

double startDist = double.MaxValue;
double endDist = double.MaxValue;
for (int i = 0; i < stopLocations.Count(); i++)
{
    (float longitude, float latitude) stopLoc = stopLocations[i];
    double distFromStart = Math.Pow(stopLoc.longitude - startStopLonLat.longitude,2) + Math.Pow(stopLoc.latitude - startStopLonLat.latitude,2);
    double distFromEnd = Math.Pow(stopLoc.longitude - endStopLonLat.longitude,2) + Math.Pow(stopLoc.latitude - endStopLonLat.latitude,2);
    if (distFromStart < startDist)
    {
        startDist = distFromStart;
        startId = i;
    }
    if (distFromEnd < endDist)
    {
        endDist = distFromEnd;
        endId = i;
    }
}

START_STOP_ID = startId;
END_STOP_ID = endId;
Console.WriteLine($"Start Stop Id:{START_STOP_ID} End Stop Id:{END_STOP_ID}");


TimeSpan timeSinceStartOfDay = DateTime.Now - DateTime.Today;

// Initialize starting stop
arrivalTimestamp[START_STOP_ID] = Convert.ToInt32(timeSinceStartOfDay.TotalSeconds);

Console.WriteLine("Routing...");

// Simplest CSA Implementation possible, runs while parsing the text files
using StreamReader stopTimesReader = new(Path.Combine("GtfsData", "stop_times.txt"));
string entry;
while ((entry = stopTimesReader.ReadLine()) != null)
{
    if (entry.Split(',')[0] == "trip_id") { continue; } // This is probably killing perf lol
    var (tripId,arrivalTime,departureTime,stopId) = ParseEntry(entry);
    if (!IsTripIdHappeningToday(tripId)) { continue; }
    if (tripId == prevTripId)
    {
        if (arrivalTimestamp[prevStopId] < prevDepartureTime && arrivalTimestamp[stopId] > arrivalTime)
        {
            arrivalTimestamp[stopId] = arrivalTime;
            inConnection[stopId] = (tripId,prevStopId,stopId,prevDepartureTime,arrivalTime);
        }
    }
    (prevTripId,prevArrivalTime,prevDepartureTime,prevStopId) = (tripId,arrivalTime,departureTime,stopId);
}

Console.WriteLine("Transit Routing Done!");

List<(string tripId,int depStop,int arrStop,int depTime,int arrTime)> tripConnections = new List<(string tripId,int depStop,int arrStop,int depTime,int arrTime)>();

TraversePath(inConnection[END_STOP_ID],0);

void TraversePath((string tripId,int depStop,int arrStop,int depTime,int arrTime) connection,int depth)
{
    if (connection.arrStop != START_STOP_ID && connection.arrStop != 0)
    {
        depth++;
        tripConnections.Add(connection);
        TraversePath(inConnection[connection.depStop],depth);
    }
}

Console.WriteLine("Connection Traversal Done!");

(string tripId,int depStop,int arrStop,int depTime,int arrTime) currentConnection = ("",0,0,0,0);
int tripLegCount = 0;
int tripTimeInSeconds = 0;
for (int i = tripConnections.Count - 1; i >= 0 ; i--)
{
    var connection = tripConnections[i];
    if (currentConnection.tripId == "")
    {
        // initialize first connection
        tripTimeInSeconds = connection.depTime; // Set trip time to departure time
        currentConnection = connection;
    }
    if (connection.tripId == currentConnection.tripId)
    {
        //If were currently walking within a connection
        currentConnection.arrStop = connection.arrStop;
        currentConnection.arrTime = connection.arrTime;
    }
    else {
        tripLegCount+=1;
        // We walked out of the current connection
        Console.WriteLine($"[{routeShortNames[tripId2RouteId[currentConnection.tripId]]} {tripId2TripHeadsign[currentConnection.tripId]}][{currentConnection.tripId}] {stopNamesEn[currentConnection.depStop]}[{stopCodes[currentConnection.depStop]}] -> {stopNamesEn[currentConnection.arrStop]}[{stopCodes[currentConnection.arrStop]}], {SecondsToString(currentConnection.depTime)} -> {SecondsToString(currentConnection.arrTime)}");
        // Reintialize the current connection for the new trip leg
        currentConnection = connection;
    }
}
// This is the last leg of the trip
tripLegCount+=1;
tripTimeInSeconds = currentConnection.arrTime - tripTimeInSeconds;
// I guess we didnt find a route
if (currentConnection == ("",0,0,0,0))
{
    Console.WriteLine("[]Failed to find a route, footpath transfers are not implemented!");
}
Console.WriteLine($"[{routeShortNames[tripId2RouteId[currentConnection.tripId]]} {tripId2TripHeadsign[currentConnection.tripId]}][{currentConnection.tripId}] {stopNamesEn[currentConnection.depStop]}[{stopCodes[currentConnection.depStop]}] -> {stopNamesEn[currentConnection.arrStop]}[{stopCodes[currentConnection.arrStop]}], {SecondsToString(currentConnection.depTime)} -> {SecondsToString(currentConnection.arrTime)}");

Console.WriteLine($"Trip time: {SecondsToString(tripTimeInSeconds)} Legs: {tripLegCount}");

/*
foreach (var connection in tripConnections)
{
    Console.WriteLine($"[{connection.tripId}] {stopNames[connection.depStop]}[{connection.depStop}] -> {stopNames[connection.arrStop]}[{connection.arrStop}], {SecondsToString(connection.depTime)} -> {SecondsToString(connection.arrTime)}");
}tripId2RouteId
*/
bool IsTripIdHappeningToday(string tripId)
{
    int serviceId = tripId2ServiceId[tripId];
    (bool sunday,bool monday,bool tuesday,bool wednesday,bool thursday,bool friday,bool saturday,DateTime startTime, DateTime endTime) calendarEntry = calendar[serviceId];
    DateTime currentDateTime = DateTime.Now;
    // if the current time is outside the start and end dates
    if (currentDateTime < calendarEntry.startTime || currentDateTime > calendarEntry.endTime)
    {
        return false;
    }
    switch (currentDateTime.DayOfWeek)
    {
        case DayOfWeek.Sunday:
            return calendarEntry.sunday;
        case DayOfWeek.Monday:
            return calendarEntry.monday;
        case DayOfWeek.Tuesday:
            return calendarEntry.tuesday;
        case DayOfWeek.Wednesday:
            return calendarEntry.wednesday;
        case DayOfWeek.Thursday:
            return calendarEntry.thursday;
        case DayOfWeek.Friday:
            return calendarEntry.friday;
        case DayOfWeek.Saturday:
            return calendarEntry.saturday;
    }
    // No idea how you could end up here
    return false;

}

(string tripId, int arrivalTime, int departureTime, int stopId) ParseEntry(string entry)
{
    string[] entrySliced = entry.Split(',');
    string tripId = entrySliced[0];
    int arrivalTime = ParseTime(entrySliced[1]);
    int departureTime = ParseTime(entrySliced[2]);
    if (departureTime == arrivalTime) // When the departure and arrival time are the same it makes the user wait for the next bus instead of just staying
    {
        departureTime+=1;
    }
    int stopId = int.Parse(entrySliced[3]);
    return (tripId,arrivalTime,departureTime,stopId);
}

int ParseTime(string time)
{
    // israel gtfs uses a 26 hour time window for each day(aka all of them overlap from 00:00 to 02:00)
    // So we gotta parse this ourselves and convert it to seconds within that 26 hour day
    string[] timeSplit = time.Split(':');
    int hours = int.Parse(timeSplit[0]);
    int minutes = int.Parse(timeSplit[1]);
    int seconds = int.Parse(timeSplit[2]);
    return (hours * 3600) + (minutes * 60) + seconds;
}

string SecondsToString(int time)
{
    int hours = time / 3600;
    int minutes = (time % 3600) / 60;
    int seconds = time % 60;
    return $"{hours.ToString("D2")}:{minutes.ToString("D2")}:{seconds.ToString("D2")}";
}