// See https://aka.ms/new-console-template for more information


Console.WriteLine("Hello, World!");


/*
 * 
 * 7.4 Parking Lot: Design a parking lot using object-oriented principles.
SOLUTION
pg 727
The wording of this question is vague, just as it would be in an actual interview. This requires you to have a conversation with your interviewer about what types of vehicles it can support, whether the parking lot has multiple levels, and so on.
For our purposes right now, we'll make the following assumptions. We made these specific assumptions to add a bit of complexity to the problem without adding too much. If you made different assumptions, that's totally fine.

The parking lot has multiple levels. Each level has multiple rows of spots. The parking lot can park motorcycles, cars, and buses.
The parking lot has motorcycle spots, compact spots, and large spots.
A motorcycle can park in any spot.
A car can park in either a single compact spot or a single large spot.
Abus can park in five large spots that are consecutive and within the same row. It cannot park in small
spots.

 * 
 */

/*** BOOK ***/

public enum VehicleSize { Motorcycle, Compact, Large }

public abstract class Vehicle
{

	protected List<ParkingSpot> ParkingSpots = new List<ParkingSpot>();
	protected string LicensePlate;
	protected int SpotsNeeded;
	protected VehicleSize Size;

	public int GetSpotsNeeded() { return SpotsNeeded; }
	public VehicleSize GetSize() { return Size; }
	public void ParkInSpot(ParkingSpot s) { ParkingSpots.Add(s); }
	public void ClearSpots() { /***/ }
	public abstract bool CanFitInSpot(ParkingSpot spot);
}


public class Bus : Vehicle {

	public Bus() {
		SpotsNeeded = 5;
		Size = VehicleSize.Large;
	}

	/*Checks if the spot is a large, Doen't check num spots*/
	public override bool CanFitInSpot(ParkingSpot spot) { /**/ return true; }
}

public class Car : Vehicle
{

	public Car()
	{
		SpotsNeeded = 1;
		Size = VehicleSize.Compact;
	}

	/*Checks if the spot is a large, Doen't check num spots*/
	public override bool CanFitInSpot(ParkingSpot spot) { /**/ return true; }
}

public class Motorcycle : Vehicle
{

	public Motorcycle()
	{
		SpotsNeeded = 1;
		Size = VehicleSize.Motorcycle;
	}

	/*Checks if the spot is a large, Doen't check num spots*/
	public override bool CanFitInSpot(ParkingSpot spot) { /**/ return true; }
}

public class ParkingLot {

	private Level[] levels;
	private const int NUM_LEVELS = 5;

	public ParkingLot() { /***/ }

	/* park the vehicle in a spot (or multiple spots) return false is failed */
	public bool ParkVehicle(Vehicle vehicle) { /***/ return true; }

}

public class Level {

	private int Floor;
	private ParkingSpot[] spots;
	private int AvailableSpots = 0; //number of free spots
	private const int SPOTS_PER_ROW = 10;

	public Level(int flr, int numberSpots) {
		/****/
	}

	public int GetAvailableSpots() { return AvailableSpots; }

	/* Find a place to park the vehicle, Return false if failed */
	public Boolean ParkVehicle(Vehicle vehicle) { return true; }

	/* Park a vehicle starting at the spot number, and continueing until vehicle.spotsNeeded */
	private bool ParkStartingAtSpot(int num, Vehicle vehicle) { /****/ return true; }

	/* Find a spot to park this vehicle, return index of spot or -1 for failure */
	private int FindAvailableSpots(Vehicle vehicle) { return -1; }

	/* When a car was removed from the spot, increment available spots */
	public void SpotFreed() { AvailableSpots++; }
}

public class ParkingSpot {

	private Vehicle VehicleInSpot;
	private VehicleSize SpotSize;
	private int Row;
	private int SpotNumber;
	private Level ParkingLevel;

	public ParkingSpot(Level lvl, int r, int n, VehicleSize s) {/****/ }

	public bool IsAvailable() { return VehicleInSpot == null; }

	/* Check if the spot is big enough and is available */
	public bool CanFitVehicle(Vehicle vehicle) { /******/ return true;  }
	public bool Park(Vehicle v) { /***/ return true; }
	public int GetRow() { return Row; }
	public int GetSpotNumber() { return SpotNumber; }
	public void RemoveVehicle() { /*****/ }
}










