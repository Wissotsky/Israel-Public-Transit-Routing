// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

using StreamReader stopTimesReader = new("GtfsData\\stop_times.txt");
//using StreamReader stopsReader = new("GtfsData\\stops.txt");

const int STOPS_COUNT = 51000; // The highest stop id seems to be at 51k
const int START_STOP_ID = 21271; // code 54135
const int END_STOP_ID = 13499; //code 25380

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

//string stopEntry;
//while ((stopEntry = stopsReader.ReadLine()) != null)
//{
//    Console.WriteLine(stopEntry);
//}

string previousEntry = "0,0,0,0"; // I need to write a parser that atleast pretends to be robust
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
        if (arrivalTimestamp[prevStopId] < ParseTime(prevDepartureTime) && arrivalTimestamp[stopId] > ParseTime(arrivalTime))
        {
            arrivalTimestamp[stopId] = ParseTime(arrivalTime);
            inConnection[stopId] = (tripId,prevStopId,stopId,ParseTime(prevDepartureTime),ParseTime(arrivalTime));
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
        Console.WriteLine($"[{connection.tripId}] {connection.depStop} -> {connection.arrStop}, {connection.depTime}:{connection.arrTime}");
        TraversePath(inConnection[connection.depStop],depth);
    }
}

(string tripId, string arrivalTime, string departureTime, int stopId) ParseEntry(string entry)
{
    string[] entrySliced = entry.Split(',');
    string tripId = entrySliced[0];
    string arrivalTime = entrySliced[1];
    string departureTime = entrySliced[2];
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