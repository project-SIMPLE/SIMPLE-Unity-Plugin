# 1. Install the Unity Package

This chapter will show you how to install the SIMPLE Unity Plugin in a Unity project and prepare the
scene for GAMA communication.

## 1.1 Create and Open a Unity Project

Start by creating a new Unity project.

![Create a new Unity project](../images/tutorial/01-create-new-unity-project.png)

Check the choosen Unity version and Create project (you don't have to choose a perticular kind of project).

![Unity version and project creation](../images/tutorial/01-unity-version-create-project.png)

Wait until Unity finishes building the scene...

![Wait while Unity builds the preview](../images/tutorial/03-wait-preview-building.png)

After the project opens, you should be on the Unity home/editor screen.

![Unity project home](../images/tutorial/01-unity-home.png)

## 1.2 Install the Package...
### ...From GitHub

1. Open the Package Manager from Unity.

![Open Package Manager from Unity](../images/tutorial/Capture%20d'%C3%A9cran%202026-0dfgedfgdfg6-18%20161333.png)

2. Click the **+** button.

<img width="281" height="297" alt="01-package-manager-add-button" src="https://github.com/user-attachments/assets/0e5360b2-1312-4d41-a5ec-32eec09ae94c" />

3. Select **Add package from git URL...**.

4. Enter:

```text
https://github.com/project-SIMPLE/SIMPLE-Unity-Plugin.git
```

To install a specific branch:

```text
https://github.com/project-SIMPLE/SIMPLE-Unity-Plugin.git#branch-name
```
![Add package from Git URL](../images/tutorial/01-package-manager-git-url.png)

5. After installation, the package should appear in the Package Manager.

![Package installed](../images/tutorial/01-package-installed.png)

### ...From Local Disk

For local development:

1. After clicking on **+** select **Add package from disk...**
2. Select the package `package.json` file from your local package folder.

## 1.3 Setup The Unity Scene

1. Open **GAMA > GAMA Panel**

![Open a new GAMA tab](../images/tutorial/Capture%20d'%C3%A9cran%2020fsfsdfsz26-06-18%20161411.png)

2. Click **Setup Scene**

![Setup Scene button](../images/tutorial/Capture%20d'%C3%A9cran%202026-06-18%20161833.png)


3. After a quick build...
![Unity project ready](../images/tutorial/01-unity-project-ready.png)

...your scene should contain every object needed to communicate with the middleware.
You can verify that the scene contains:
   - a player or camera rig;
   - a `Connection Manager`;
   - a `Game Manager`;
   - required scene roots for preview and runtime objects.
     
<img width="756" height="347" alt="Capture d&#39;écran 2026-06-18 161318" src="https://github.com/user-attachments/assets/70400ebe-86b5-446f-be3d-46d86ee09619" />

## Result

At the end of this chapter, Unity is ready to communicate with the middleware.
