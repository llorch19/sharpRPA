
# sharpRPA
# Please note this project is currently in an Alpha (preview) state and subject to change.
sharpRPA is a Free, Open-Source, and Easy-To-Use RPA (Robotic Process Automation) solution built on .NET 10 in C#.  sharpRPA allows you to automate straight-forward and repetative actions with ease - Don't Write Code, Build Functionality!

# Build
Requires the .NET 10 SDK on Windows (or `EnableWindowsTargeting` for cross-platform builds).

```
dotnet build sharpRPA.sln
```

Browser automation uses Microsoft Playwright. Install the Chromium browser before running browser commands:

```
pwsh sharpRPA/bin/Debug/net10.0-windows10.0.19041.0/playwright.ps1 install chromium
```

# Start Screen
![Main App Window](https://i.imgur.com/GkYEivM.png)

# Finished Script Examples
## Here are a few examples of sample scripts that have been built.
![Script Example1](https://i.imgur.com/j5HRt20.png)
![Script Example2](https://i.imgur.com/krFU9M1.png)


### Request a Feature
Don't see an automation feature you are looking for?  [Create a new enhancement issue!](https://github.com/saucepleez/sharpRPA/issues/new)

### Don't want to build your own?
Click [here](https://github.com/saucepleez/sharpRPA/releases) to view the latest releases.  Release binaries are built in Visual Studio 'Release' mode and packaged into a .ZIP file.  Simply extract to a folder, double-click and run to get started.
