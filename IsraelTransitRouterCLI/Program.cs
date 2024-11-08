// See https://aka.ms/new-console-template for more information
using System.Text;

Console.WriteLine("Starting...");

Console.OutputEncoding = new UTF8Encoding(); // Fix hebrew rendering even though well probably move to english

const int STOPS_COUNT = 51000; // The highest stop id seems to be at 51k
const int ROUTES_COUNT = 40000; // Highest route Id is at about 40k
const int START_STOP_ID = 21271; // code 54135
const int END_STOP_ID = 13499; //code 25380

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
arrivalTimestamp[START_STOP_ID] = 0;

// Load translations.txt
Dictionary<string,string> translationsTable = new Dictionary<string, string>();
//Dictionary<string,List<string>> wordTranslationsTable = new Dictionary<string, List<string>>();

using StreamReader translationsReader = new("GtfsData\\translations.txt");

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

using StreamReader stopsReader = new("GtfsData\\stops.txt");

string stopEntry;
while ((stopEntry = stopsReader.ReadLine()) != null)
{
    string[] splitEntry = stopEntry.Split(',');
    if (splitEntry[0] == "stop_id") { continue; }

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
            Console.WriteLine($"[TRANSLATION ERROR] {stopName} not found");
        }
        stopNameEn = stopName;
    }

    stopCodes[stopId] = stopCode;
    stopNames[stopId] = stopNameEn;

}

Console.WriteLine("Stops Loading Done!");

// Load trips.txt
Dictionary<string,int> tripId2RouteId = new Dictionary<string,int>();
Dictionary<string,int> tripId2ServiceId = new Dictionary<string,int>();
Dictionary<string,string> tripId2TripHeadsign = new Dictionary<string, string>();

using StreamReader tripsReader = new("GtfsData\\trips.txt");

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
                Console.WriteLine($"[TRANSLATION ERROR] {tripHeadsignPart} not found");
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

using StreamReader routesReader = new("GtfsData\\routes.txt");

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

string previousEntry = "0,00:00:00,00:00:01,0"; // I need to write a parser that atleast pretends to be robust
var (prevTripId,prevArrivalTime,prevDepartureTime,prevStopId) = ParseEntry(previousEntry);

// Simplest CSA Implementation possible, runs while parsing the text files
using StreamReader stopTimesReader = new("GtfsData\\stop_times.txt");
string entry;
while ((entry = stopTimesReader.ReadLine()) != null)
{
    var (tripId,arrivalTime,departureTime,stopId) = ParseEntry(entry);
    if (tripId == "stop_id") { continue; }
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
        Console.WriteLine($"[{routeShortNames[tripId2RouteId[currentConnection.tripId]]} {tripId2TripHeadsign[currentConnection.tripId]}] {stopNames[currentConnection.depStop]}[{stopCodes[currentConnection.depStop]}] -> {stopNames[currentConnection.arrStop]}[{stopCodes[currentConnection.arrStop]}], {SecondsToString(currentConnection.depTime)} -> {SecondsToString(currentConnection.arrTime)}");
        // Reintialize the current connection for the new trip leg
        currentConnection = connection;
    }
}
// This is the last leg of the trip
tripLegCount+=1;
tripTimeInSeconds = currentConnection.arrTime - tripTimeInSeconds;
Console.WriteLine($"[{routeShortNames[tripId2RouteId[currentConnection.tripId]]} {tripId2TripHeadsign[currentConnection.tripId]}] {stopNames[currentConnection.depStop]}[{stopCodes[currentConnection.depStop]}] -> {stopNames[currentConnection.arrStop]}[{stopCodes[currentConnection.arrStop]}], {SecondsToString(currentConnection.depTime)} -> {SecondsToString(currentConnection.arrTime)}");

Console.WriteLine($"Trip time: {SecondsToString(tripTimeInSeconds)} Legs: {tripLegCount}");
/*
foreach (var connection in tripConnections)
{
    Console.WriteLine($"[{connection.tripId}] {stopNames[connection.depStop]}[{connection.depStop}] -> {stopNames[connection.arrStop]}[{connection.arrStop}], {SecondsToString(connection.depTime)} -> {SecondsToString(connection.arrTime)}");
}
*/

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