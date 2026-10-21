TaraKanteen - ASP.NET Core + SQLite

Project layout:
  Program.cs          ASP.NET Core API and SQLite setup
  app.db              SQLite database (Users table)
  wwwroot/index.html
  wwwroot/login.html
  wwwroot/register.html

IMPORTANT:
- The uploaded HTML files reference style.css, but style.css was not included in the uploaded files.
  Copy your existing style.css into wwwroot/style.css to preserve the design.
- app.db is stored in the project root, outside wwwroot, so it is not served publicly.
- Do not host this project by double-clicking the HTML files. Run the ASP.NET Core server.

Run on Windows:
1. Install the .NET 8 SDK.
2. Open PowerShell in this folder.
3. Run:
     dotnet restore
     dotnet run
4. Open the local URL printed by dotnet run (usually https://localhost:xxxx or http://localhost:xxxx).
5. Open /register.html, create a user, then use /login.html.

How it works:
- register.html POSTs JSON to /api/register.
- Program.cs hashes passwords and stores them in the Users table in app.db.
- login.html POSTs JSON to /api/login.
- Successful login creates an HTTP-only authentication cookie.

The existing app.db is included. Program.cs uses EnsureCreatedAsync, which creates the schema
if missing but does not migrate an existing schema. Back up app.db before changing database structure.
