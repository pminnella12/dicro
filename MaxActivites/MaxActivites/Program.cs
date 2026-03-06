// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

/**

Activity StartTime Problem

**/


ActivityStartTime at = new ActivityStartTime();

var output = at.GetMaxActivities();
Console.WriteLine(output.ToString());
Console.Read();

public class ActivityStartTime
{

	private struct Activity
	{
		public int start;
		public int end;
		public Activity(int s, int e)
		{
			start = s;
			end = e;
		}
	}
	private List<Activity> Activities;

	public ActivityStartTime()
	{

		Activities = new List<Activity>();
		Activities.Add(new Activity(2, 3));
		Activities.Add(new Activity(1, 4));
		Activities.Add(new Activity(5, 8));
		Activities.Add(new Activity(6, 10));

	}

	public int GetMaxActivities()
	{


		//Activities = Activities.OrderBy(x => x.end).ToList();
		var helper = new List<Activity>();
		var high = Activities.Count() - 1;
		MergeSort(Activities, helper, 0, high);
		int finishTime = Activities[0].end;
		int totalCount = 1;
		for (int i = 1; i <= Activities.Count() - 1; i++)
		{
			if (Activities[i].start >= finishTime)
			{
				totalCount++;
				finishTime = Activities[i].end;
			}
		}

		return totalCount;

	}

	private void MergeSort(List<Activity> act, List<Activity> helper, int low, int high ) {

		if (low < high) {

			int middle = (low + high) / 2;
			MergeSort(act, helper, low, middle);
			MergeSort(act, helper, middle + 1, high);
			Merge(act, helper, low, middle, high);
		}
	}

	private void Merge(List<Activity> act, List<Activity> helper, int low, int middle, int high) {
		for (int i = 0; i <= high; i++) {

			helper.Add(act[i]);
		}


		int helperLeft = low;
		int helperRight = middle + 1;
		int current = low;

		while (helperLeft <= middle && helperRight <= high) {

			if (helper[helperLeft].end <= helper[helperRight].end)
			{
				act[current] = helper[helperLeft];
				helperLeft++;
			}
			else {
				act[current] = helper[helperRight];
				helperRight++;
			}
		}

		int remaining = middle - helperLeft;
		for (int i = 0; i <= remaining; i++)
		{
			act[current + i] = helper[helperLeft + i];
		}
	}
}
