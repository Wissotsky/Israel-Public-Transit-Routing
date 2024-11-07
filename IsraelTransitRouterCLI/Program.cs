// See https://aka.ms/new-console-template for more information
using System.Text;

Console.WriteLine("Hello, World!");

using StreamReader stopTimesReader = new("GtfsData\\stop_times.txt");

Console.OutputEncoding = new UTF8Encoding(); // Fix hebrew rendering even though well probably move to english

const int STOPS_COUNT = 51000; // The highest stop id seems to be at 51k
const int START_STOP_ID = 21271; // code 54135
const int END_STOP_ID = 13499; //code 25380


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

using StreamReader translationsReader = new("GtfsData\\translations.txt");

string translationEntry;
while ((translationEntry = translationsReader.ReadLine()) != null)
{
    // hebrew_string,lang,lang_string
    string[] splitTranslation = translationEntry.Split(','); // Ideally we would be using a proper csv parsing library, but I want to keep it as dumb and simple as possible
    if (splitTranslation[1] == "EN")
    {
        string hebrewString = splitTranslation[0].Replace("''","\""); // Make sure gershayim are consistent
        if (hebrewString.Contains("האמנים/עוזי"))
        {
            Console.WriteLine(hebrewString);
        }
        string langString = splitTranslation[2];
        translationsTable.TryAdd(hebrewString,langString);
    }
}


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
        Console.WriteLine($"[TRANSLATION ERROR] {stopName} not found");
        stopNameEn = stopName;
    }

    stopCodes[stopId] = stopCode;
    stopNames[stopId] = stopNameEn;

}

string previousEntry = "0,00:00:00,00:00:01,0"; // I need to write a parser that atleast pretends to be robust
var (prevTripId,prevArrivalTime,prevDepartureTime,prevStopId) = ParseEntry(previousEntry);

string entry;
while ((entry = stopTimesReader.ReadLine()) != null)
{
    var (tripId,arrivalTime,departureTime,stopId) = ParseEntry(entry);
    if (tripId == "stop_id") { continue; }
    if (tripId == prevTripId)
    {
        // print the transit connection
        // We parse the times only when its valid
        //Console.WriteLine($"[{tripId}] {prevStopId} {stopId} {ParseTime(prevDepartureTime)} {ParseTime(arrivalTime)}");
        if (arrivalTimestamp[prevStopId] < prevDepartureTime && arrivalTimestamp[stopId] > arrivalTime)
        {
            arrivalTimestamp[stopId] = arrivalTime;
            inConnection[stopId] = (tripId,prevStopId,stopId,prevDepartureTime,arrivalTime);
        }
    }
    //Console.WriteLine($"[{tripId}] Stop:{stopId} {arrivalTime} -> {departureTime}");
    (prevTripId,prevArrivalTime,prevDepartureTime,prevStopId) = (tripId,arrivalTime,departureTime,stopId);
}

Console.WriteLine("CSA Done!");
Console.WriteLine(inConnection[END_STOP_ID].tripId);
//List<(string,int,int,int,int)> tripConnections = new List<(string,int,int,int,int)>();

TraversePath(inConnection[END_STOP_ID],0);

void TraversePath((string tripId,int depStop,int arrStop,int depTime,int arrTime) connection,int depth)
{
    if (connection.arrStop != START_STOP_ID && connection.arrStop != 0)
    {
        depth++;
        Console.WriteLine($"[{connection.tripId}] {stopNames[connection.depStop]}[{connection.depStop}] -> {stopNames[connection.arrStop]}[{connection.arrStop}], {SecondsToString(connection.depTime)} -> {SecondsToString(connection.arrTime)}");
        TraversePath(inConnection[connection.depStop],depth);
    }
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