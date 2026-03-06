// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");


/*

Parking Lot: Design a parking lot using object-oriented principles. Hints: #258

handle ambiguity - 
	multiple levels
	park motorcycles, cars, buses
	has motorcylce spots, compact spots, large spots
	motorcycle can park in any 317
	car can park in compact spot or large spot
	bus can park in 5 large spots that are consecutive and within same row, it cannot park in small spots


Define the core objects-
	parking lot
		level
		rows (spots per row, small, medium, large)
		spot
	motocycle
	cars
	buses

Anaylze Relation ships
	parking lot can have multiple levels
	each level can have multiple rows
	each row can have multple spot types

	queue of vehicles to park
	vehicle can be parked 

investigate actions
	is spot available
	park vehicle (in optimal spot)
	remove vehicle (parked, leave)
7
*/
public enum SpotSize
{
	MotorcycleSpot,
	CompactSpot,
	LargeSpot
}

public interface IVehicle
{
	void SetParkedStatus(bool parked);
}

public abstract class Vehicle : IVehicle
{

	protected bool Parked { get; set; }
	protected int MotorcycleSpots { get; }
	protected int CompactSpots { get; }
	protected int LargeSpots { get; }


	public Vehicle(int small, int medium, int large, bool parked = false)
	{
		Parked = parked;
		MotorcycleSpots = small;
		CompactSpots = medium;
		LargeSpots = large;

	}

	public void SetParkedStatus(bool parked)
	{
		Parked = parked;
	}
}

public interface IParkingLot
{
	bool TryPark(Vehicle vehicle);
}

public class ParkingLot : IParkingLot
{

	private Level[] ParkingLevels { get; }
	public ParkingLot(Level[] levels)
	{
		ParkingLevels = levels;
	}


	public bool TryPark(Vehicle vehicle) { /**/ return true; }


}

public class MotorCycle : Vehicle
{

	public MotorCycle() : base(1, 1, 1, false) { }
}


public class Car : Vehicle
{

	public Car() : base(0, 1, 1, false) { }
}


public class Bus : Vehicle
{

	public Bus() : base(0, 0, 5, false) { }
}

public interface IParkingSpot
{
	void SetVechcleInSport(Vehicle vehicle);
	void RemoveVechcleInSport();
}

public class ParkingSpot : IParkingSpot
{

	public Vehicle VehicleInSpot { get; set; }
	public int SpotId { get; }
	public SpotSize Size { get; }


	public ParkingSpot(SpotSize size, int spotId)
	{
		Size = size;
		SpotId = spotId;
	}

	public void SetVechcleInSport(Vehicle vehicle)
	{
		VehicleInSpot = vehicle;
	}

	public void RemoveVechcleInSport()
	{
		VehicleInSpot = null;
	}
}

public interface IParkingRow
{
	ParkingSpot[] IsSpotAvailable(Vehicle vehicle);	 
	bool RowFull();
}

public class ParkingRow : IParkingRow
{

	public IParkingSpot[] Row { get; }
	public int RowId { get; }
	public ParkingRow(IParkingSpot[] parkingSpots, int rowId)
	{
		Row = parkingSpots;
		RowId = rowId;
	}

	//if spot count more than one they must be in consecutive spot in array
	public ParkingSpot[] IsSpotAvailable(Vehicle vehicle) { /*...*/ return new ParkingSpot[0];}
	public bool RowFull() { /*...*/ return true;	}

}

public interface ILevel { List<ParkingRow> RowsNotFull(); }
public class Level : ILevel
{

	public ParkingRow[] Rows { get; }
	Level(ParkingRow[] rows)
	{
		Rows = rows;
	}

	public List<ParkingRow> RowsNotFull()
	{

		List<ParkingRow> rowsNotFull = new List<ParkingRow>();
		for (int i = 0; i < Rows.Length; i++)
		{
			if (!Rows[i].RowFull())
			{
				rowsNotFull.Add(Rows[i]);
			}
		}

		return rowsNotFull;
	}

}

