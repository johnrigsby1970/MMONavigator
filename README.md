# MMONavigator

Program to provide directions when given destination and current coordinates in x z y format.

## Description

I play an online game that copies the current location coordinates to the clipboard when
a user executes a command. This program will monitor the clipboard and use the current location  
along with an entered destination location to determine the compass heading necessary to go from the current location
to the destination location.

Destination: 0 0 100
Location:    0 50 0 0

Result: N 0° 100  (green)

* Your destination location is x:0 y:100 and your current location is x:0 y:0 (center of coordinate system) 
therefore you need to face North and the distance between you and the destination is 100 units of measure. 
* You are at an elevation of 50, which is ignored by this program at this time.
* You are facing North, so the text will be highlighted in green.

## Getting Started

### Dependencies

* This program is coded to run on Windows 10 and above using .Net 8.

### Installing

* Download the zip file and locate the MMONavigator.exe program in the bin/release folder
* You will need the .net 8 desktop runtime installed. If prompted, download and install the runtime.

### Executing program

* Execute MMONavigator.exe. You will be told the program should not be trusted. This program is not signed with 
a code signing certificate. Trust it or not. That is up to you. Inspect and compile the code yourself from this 
repository.

## Help

The program expects coordinates in the following format:
x z y f

where f is the facing in compass degrees.

2134 592 -567 0

c:2134
y:-567
z:592
f:0 degrees, which is North

It expects these numbers to be separated by a single space and will not process bad data. 

As you navigate your world, execute the /loc or other command to cause the coordinates to be written to the clipboard. 
This program will watch the clipboard and adjust the suggested heading. 

The program will remain always on top in Windows. So if your game goes full screen this program will remain visible.
It is not part of your game, is not reading packets from your game. If your game does not write to the clipboard, you 
may copy the location yourself with CTRL + C to cause the lcoation to be saved to the clipboard yourself.

When the suggested heading is within 2 degrees one way or the other, the text will turn green. A little more off and it 
will be lighter green, and then yellow, and then white. 

Execute the location command or otherwise cause the location to be saved to the clipboard to cause the direction to 
update.

You can enter the desitnation and current location manually and the direction will udpate on each change. You do not
need to run a game to run this program. It can be used with any system that has x, y coordinates.

If you do not have z (elevation) you can put in any number or leave it blank and only enter 2 numbers for x,y. The 
program does not use z and f directly and supports a set of two, three, or four numbers.

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