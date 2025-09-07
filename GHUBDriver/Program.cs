using GHUBDriver.Classes;

Console.WriteLine("Mouse should move...");

GHub ghub = new();
ghub.UpdateMouse(0, 25, 0, 0);
ghub.UpdateMouse(0, 0, 25, 0);

Console.ReadLine();