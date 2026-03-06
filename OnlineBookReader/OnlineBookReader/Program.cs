// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

/*

Online Book Reader - Design the data structures for an online book reader system.


Since the problem doesn't describe much about the functionality, let's assume we want to design a basic online reading system which provides the following functionality:
• User membership creation and extension.
• Searching the database of books.
• Reading a book.
Only one active user at a time
Only one active book by this user.
To implement these operations we may require many other functions, like get, set, update, and so on.
The objects required would likely include User, Book, and Library.



*/

/*
public interface ILibrary
{
	Task<Book[]> SearchBooks(string category, string searchText);
	Task<User> CheckoutBook(int userId, int bookId);
	Task<bool> ExtendCheckout(int userId);
}
public class Library : ILibrary
{

	public readonly IBookRepository BookRepo;
	public readonly IUserRepository UserRepo;

	public Library(IBookRepository bookRepo, IUserRepository userRepo)
	{
		BookRepo = bookRepo;
		UserRepo = userRepo;

	}

	public async Task<Book[]> SearchBooks(string category, string searchText)
	{

		return await BookRepo.SearchBooksAsync(category, searchText);
	}

	public async Task<User> CheckoutBook(int userId, int bookId)
	{

		//retruns false if userId is not valid or already checked out book
		//can be updated to automatically return book if checked out
		return await BookRepo.CheckoutBookAsync(userId, bookId);
	}

	public async Task<bool> EntendCheckout(int userId)
	{
		//retruns false if check cannot be extended
		return await BookRepo.EntendCheckout(userId);

	}
}


public class UserManager
{

	private readonly IUserRepository UserRepo;
	private readonly ILibrary Library;
	private readonly IBookDisplay BookDisplay;
	private readonly User CurrentUser;

	public async Task UserManager(IUserRepository userRepo, ILibrary library, IBookDisplay bookDisplay, int userId)
	{
		UserRepo = userRepo;
		Library = library;
		BookDisplay = bookDisplay;
		Task<User> userTask = GetUser(userId);
		CurrentUser = await userTask;
	}

	private async Task<User> GetUser(int userId)
	{
		return await UserRepo.GetUserAsync(userId);
	}

	public async Task<User> CreateAccount(string username, string password, string email)
	{
		return await UserRepo.CreateAccount(username, password, email);
	}

}

public class BookDisplay
{

	public readonly Book Book;

	public BookDisplay(IBookRepository bookRepo)
	{
		BookRepo = bookRepo;
	}

	public string GetPageData(int page)
	{
		return Book.GetPageData(page);
	}
}

public class User
{
	string UserName { get; }
	string Details { get; }

}

public class Book {

	string BookName { get; }
	string Details { get; }
}
*/



/*
 * BOOK
 */


public class OnlineReaderSystem
{
	private Library library;
	private UserManager userManager;
	private Display display;

	private Book activeBook;
	private User activeUser;

	public OnlineReaderSystem()
	{
		userManager = new UserManager();
		library = new Library();
		display = new Display();
	}

	public Library getLibrary() { return library; }
	public UserManager getUserManager() { return userManager; }
	public Display getDisplay() { return display; }

	public Book getActiveBook() { return activeBook; }
	public void setActiveBook(Book book)
	{
		activeBook = book;
		display.displayBook(book);
	}

	public User getActiveUser() { return activeUser; }
	public void setActiveUser(User user)
	{
		activeUser = user;
		display.displayUser(user);
	}
}


public class Library
{

	private Dictionary<int, Book> books;

	public Book addBook(int id, String details)
	{
		if (books.ContainsKey(id)) { return null; }

		Book book = new Book(id, details);
		books.Add(id, book);
		return book;
	}

	public bool remove(Book b) { return remove(b.getId()); }
	public bool remove(int id)
	{
		if (!books.ContainsKey(id)) { return false; }

		books.Remove(id);
		return true;
	}

	public Book find(int id) { return books[id]; }
}

public class UserManager
{

	private Dictionary<int, User> users;

	public User addUser(int id, string details, int accountType)
	{
		if (users.ContainsKey(id)) return null;

		User user = new User(id, details, accountType);
		users.Add(id, user);
		return user;
	}

	public User find(int id) { return users[id]; }
	public bool remove(User u) { return remove(u.UserID); }
	public bool remove(int id)
	{
		if (!users.ContainsKey(id))
		{
			return false;
		}

		users.Remove(id);
		return true;
	}
}

public class Display
{

	private Book activeBook;
	private User activeUser;
	private int pageNumber = 0;

	public void displayUser(User user)
	{
		activeUser = user;
		refreshUserName();
	}

	public void displayBook(Book book)
	{
		pageNumber = 0;
		activeBook = book;

		refreshTitle();
		refreshDetails();
		refreshPage();
	}

	public void turnPageForward()
	{
		pageNumber++;
		refreshPage();
	}

	public void turnPageBackward()
	{
		pageNumber--;
		refreshPage();
	}

	public void refreshUserName() {/* updates username display */}
	public void refreshTitle() {/* updates title display */}
	public void refreshDetails() {/* updates details display */}
	public void refreshPage() {/* updates page display */}
}


public class Book
{
	private int bookId;
	private string details;

	public Book(int id, string det)
	{
		bookId = id;
		details = det;
	}

	public int getId() { return bookId; }
	public void setId(int id) { bookId = id; }
	public string getDetails() { return details; }
	public void setDetails(string det) { details = det; }
}

public class User
{
	private int userId;
	private string details;
	private int accountType;

	public void renewMembership() { }

	public User(int id, string det, int acctType)
	{
		userId = id;
		details = det;
		accountType = acctType;
	}

	/*  GETTERS AND SETTERS    */

	public int UserID
	{
		get { return userId; }
		set { userId = value; }
	}

	public string Details
	{
		get { return details; }
		set { details = value; }
	}

	public int AccountType {
		get { return accountType; }
		set { accountType = value; }
	}

}










































