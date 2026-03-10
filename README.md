# MMONavigator

This app will provide you with a directional indicator from your 
current position to your destination, as defined by the coordinates 
you provide.  It will update that direction based on triggers 
initiated by you (ex: typing /loc to get your current position or using a macro to do the same, repeatedly).

## Dependencies

* This program is coded to run on Windows 10 and above using Microsoft .Net 8 runtime. 
* You will need to install the Microsoft .Net Desktop Runtime.
* Microsoft .NET Desktop Runtime can be found at https://dotnet.microsoft.com/en-us/download/dotnet/8.0

## Description

Maybe you play an online game, Pantheon - Rise of the Fallen, that copies the current player location coordinates to the clipboard when
a user executes a command, /loc. This program will monitor the clipboard and use the current location,  
along with an entered destination location, to determine the compass heading necessary to go from the current location
to the destination location. It will update every time the current location is changed. 

The current location changes every time the user enters a command. Some games, Everquest or Project 1999, write to a log file. 
You can configure this app to monitor a log file and create game profiles to handle different characters.

### Use cases

This program is useful for a number of scenarios.

* Guide to the location of a corpse after a death. Remember to type /loc, if applicable, before you revive. 
  The program will capture the location if you launch it after you revive so long as you have not executed 
  the command again after you revive.
* Guide you to a saved destination, such as the entrance of a dungeon or set of related mobs. Maybe you have a camp that you like in the middle of a forest with no real landmarks.
* Save a series of waypoints and load each destination one after the other to guide you across dangerous territory.

### Concerns

Are you cheating? No. This game does not do packet sniffing. This program can capture your location from a desired source, but that source 
is not tied directly to the game outside approved locations. After that, it can determine a direction from a location 
and destination and illustrate that on a compass.

If you know the destination and current location, you can figure all of this out in your head. Do you want to every time something gets in your way?

## Getting Started

### Installing

* Using the green <> Code button in GitHub, download the zip file of this repository.
* Download and extract the zip file to a directory on your computer. The location is not important, but you need to 
  remember where you put it.
* Build it yourself or execute the published file in the folder MMONavigator-master\bin\Release\net8.0-windows\publish.
* Locate and run the MMONavigator.exe program.
* You will need the .net 8 desktop runtime installed. If prompted, download and install the runtime.
* Windows will warn you "Don't Run!" (explained below) the first time you execute the program. Choose the "More info" link to show the "Run anyway" button.
* Click this "Run anyway" button.
* The program is set up to run using a default game profile for Pantheon - Rise of the Fallen. You may set it up to run for any game 
  that has a coordinate system. It readily works with Everquest.


### Executing program

* Execute MMONavigator.exe. As mentioned above, you will be told the program should not be trusted. This program is not signed with 
a code signing certificate. Trust it or not, but you can also inspect and compile the code yourself from this 
repository if you do not want to trust the compiled program. 100% of the code is open source and available.
* The reason for the prompt is that the compiled code is not signed with a certificate. What that means to you is that it is  
 possible a hacker could alter it after it is built and before you download it. If it is signed, a hacker cannot do that. 
* The program is built and directly uploaded to GitHub. Unless the hacker can break in to GitHub, the code is safe.
* A certificate costs $600 per year. This software is currently free to use. One that is signed would cost money, and at present, the use audience doesn't justify the cost. 

### Initial experience

* The program will appear at the top center of your screen. In a game like Pantheon - Rise of the Fallen, it will wrap around the game compass. 
* The program will be transparent and will not obscure your game except the game compass.
* The finger icon button will always show. By default, this will appear to the left of the game compass, in the case of Pantheon.
* The initial game profile is set to Pantheon - Rise of the Fallen.
* The game will watch your clipboard for location updates. Type /loc to set the location in this program.
* Set a desired destination location, and the program will update the direction to the destination.
* The program will update the direction every time the location is changed. It constantly looks for changes in location.
* Let's say you die, and there isn't a summoner available. Type /loc to get your current location. Then press the target button to the right of the 
  location to copy the location as your destination. Now revive and type /loc to capture your current location. Don't do this before you have saved the corpse location as your destination.
* The program will update the directions to guide you to your destination.

### Test it

* Type /loc to put the current location in the destination text box.
* Press the target button to copy it to your desintation.
* Run straight ahead, do not turn around.
* Type /loc to capture your current location.
* You should see the green target dot at the bottom of the compass or in games that do not have facing as part of the coordinates, it will be on the compass in the direction behind your current direction.
* If you are within 100 meters of the destination, the green target dot will be inside the compass circle.

If you can create a macro, make one that repeats /loc at regular intervals as many times as it will allow.

### Use with Everquest or Project 1999.

* Since the program is defaulted to the Pantheon style coordinate system, you will need to edit the watcher configuration to use the Everquest. Go to the gear icon and click the right most button next to the location text box. This is the watcher configuration dialog.
* Select or create the game profile you want to use.
* In Everquest the location is written to a log file. It only writes to the log file once you turn logging on in your game.
* The program will watch the log file for location updates. Type /loc to set the location in this program.
* Set the coordiante system used by your game. In the case of Everquest, the coordinate system is LeftHanded.
* Set the format of the coordinates so that the program can parse them. In the case of Everquest this is [Y-Axis], [X-Axis], [Z-Axis]. (Ex. 123.4, -567.8, 17)
* Enter a destination, or if you have a corpse, copy the location to destination, revive yourself and start pressing /loc as it guides you to your corpse.

### Additional features

* There are four buttons to allow you to start timers of 5, 10, 15, and 20-minute intervals. A system beep sound will play once you reach zero. 
* At 1 minute the button will turn from blue to yellow, and then eventually red.

## Help

* You can minimize the program to the taskbar. This program is always running and will always run on top when it is maximized. 
However, it is not directly tied to the game you are playing. So if you minimize it and want to get it back, you'll need to pop out of your game, back to the Windows task bar, to maximize it again.

* You may toggle the compass on and off, which will hide the compass and other controls until you need them again.

* When you get within 100 meters of the destination, the green target circle will move towards the center of the compass. The compass will turn from yellow to blue.

* When the suggested heading is within 2 degrees one way or the other, the text description of your desired direction will turn green. A little more off target, and it
will be lighter green, and then yellow, and then white. 

* You can enter the destination and current location manually, and the direction will update on each change. You do not
need to run a game to run this program. It can be used with any system that has x, y coordinates.

* When entering locations manually, if you do not have z (elevation) you can put in any number or leave it blank and only enter 2 numbers for x,y. The 
program does not use z and d directly and supports a set of two, three, or four numbers. You can find coordinates for a location by using https://shalazam.info/maps/1?.

### Controls

You can use buttons on the toolbar, which are hidden until you mouse over the toolbar or the finger icon. 

The finger icon lets you drag the compass around the screen to a better position.

The gear icon is for settings. It will toggle viewing the destination and location entry text boxes. 

Next to the location you will find a button to copy the current location to the destination text box. 

The button at the far right of the location text box lets you set up game profiles, which are how you configure the program to watch for changes in location. This is called the watcher configuration dialog.

You can have a game profile for Pantheon, which reads the location from the system clipboard, and a game profile for Everquest, which reads it from the log file associated to each of your characters. You would need a profile for each character in Everquest. In Pantheon, you only need the one game profile. 

You can switch between game profiles on the watcher configuration dialog.

Next to the destination text box is a button that lets you name and remember the location. The destination text box is also a drop-down list that shows you previously saved locations. You can select from this list or forget previously saved locations. Think waypoints. Load one, go to it, load the next, go to it.

For convenience, you can toggle the entire screen to hide the compass and other controls when not in use. 

The toolbar has a timer icon button that will toggle showing you four timer buttons. You can use these buttons to start timers in 5, 10, 15, and 20-minute intervals.

## Authors

John Rigsby
(https://www.observantmonkey.com)

## Version History

* 0.1
    * Initial Release

## License

This project is licensed under the MIT License - see the LICENSE.md file for details

## Acknowledgments

Inspiration, code snippets, etc.
* https://stackoverflow.com/questions/21461017/wpf-window-with-transparent-background-containing-opaque-controls
* https://stackoverflow.com/questions/55447212/how-do-i-make-a-transparent-wpf-window-with-the-default-title-bar-functionality
* https://corey255a1.wixsite.com/wundervision/single-post/simple-wpf-compass-control
* https://stackoverflow.com/questions/93650/apply-stroke-to-a-textblock-in-wpf
* https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-resource-dictionary
* https://gist.github.com/DomPizzie/7a5ff55ffa9081f2de27c315f5018afc#file-readme-template-md
