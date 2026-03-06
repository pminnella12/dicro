// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");



public class Employee
{

	int EmployeeId { get; }
	string Name { get; }
	DateTime DateJoined { get; }


	public Employee(int id, string name, DateTime dateJoined)
	{
		EmployeeId = EmployeeId;
		Name = name;
		DateJoined = dateJoined;
	}

}


public class Chef : Employee
{

	Chef(int id, string name, DateTime dateJoined) : base(id, name, dateJoined) { }

	public void PrepareDish() { }

	private void PlaceOrderInFinishedQueue() { }

	private void NotifyOrderComplete() { }
}