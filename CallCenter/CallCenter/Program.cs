// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

public enum Rank {
    Responder,
    Manager,
    Director
}
public class CallHandler {
    private readonly int LEVELS = 3;

    private readonly int NUM_RESPONDANTS = 10;
    private readonly int NUM_MANAGERS = 4;
    private readonly int NUM_DIRECTORS = 2;

    List<List<Call>> callQueues;

    public CallHandler() { /**/ }

    public Employee getHandlerForCall(Call call) { /**/ var emp = new Employee(); return emp; }

    public void dispatchCall(Caller caller) {
        Call call = new Call(caller);
        dispatchCall(call);
    }

    public void dispatchCall(Call call) {
        Employee emp = getHandlerForCall(call);
        if (emp != null)
        {
            emp.receiveCall(call);
            call.setHandler(emp);
        }
        else {
            call.reply("Please wait for free employee to reply");
            //TODO
            //callQueues.Select()
        }
    }

    public bool assignCall(Employee emp) { /**/ return true; }
}

public class Caller {
}

public class Call {
    private Rank rank;
    private Caller caller;
    private Employee handler;

    public Call(Caller c) {
        rank = Rank.Responder;
        caller = c;
    }

    public void setHandler(Employee e) { handler = e; }
    public Rank getRank() { return rank; }
    public void setRank(Rank r) { rank = r; }
    public Rank incrementRank() { /*...*/ return rank; }
    public void disconnect() { /*...*/ }

}

public abstract class Employee {
    private Call currentCall = null;
    protected Rank rank;

    public Employee(CallHandler handler) { /****/ }

    public void receiveCall(Call call) { /***/ }

    public void callCompleted() { /***/ }

    public void escalateAndReassign() { /***/ }

    public bool assignNewCall() { /***/ return true; }

    public bool isFree() { return currentCall == null; }

    public Rank getRank() { return rank; }

}

public class Director : Employee {

    public Director(CallHandler handler) : base (handler) { rank = Rank.Director; }
}

public class Manager : Employee {

    public Manager(CallHandler handler) : base(handler) { rank = Rank.Manager; }
}

public class Respondant : Employee
{

    public Respondant(CallHandler handler) : base(handler) { rank = Rank.Responder; }
}