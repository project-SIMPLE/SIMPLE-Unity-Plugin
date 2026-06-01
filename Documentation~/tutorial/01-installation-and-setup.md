# 1. Install the Unity Package

This chapter installs the SIMPLE Unity Plugin in a Unity project and prepares the
scene for GAMA communication.

## Create and Open a Unity Project

Start by creating a new Unity project.

![Create a new Unity project](../images/tutorial/01-create-new-unity-project.png)

Check the choosen Unity version and Create project (you don't have to choose a perticular kind of projetc).

![Unity version and project creation](../images/tutorial/01-unity-version-create-project.png)

Wait until Unity finishes building the scene...

![Wait while Unity builds the preview](../images/tutorial/03-wait-preview-building.png)

After the project opens, you should be on the Unity home/editor screen.

![Unity project home](../images/tutorial/01-unity-home.png)

## Install the Package...
### ...From GitHub

1. Open the Package Manager from Unity.

![Open Package Manager from Unity](../images/tutorial/01-package-manager-menu.png)

3. Click the **+** button.

![Package Manager add button](../images/tutorial/01-package-manager-add-button.png)

5. Select **Add package from git URL...**.

6. Enter:

```text
https://github.com/project-SIMPLE/SIMPLE-Unity-Plugin.git
```

To install a specific branch:

```text
https://github.com/project-SIMPLE/SIMPLE-Unity-Plugin.git#branch-name
```
![Add package from Git URL](../images/tutorial/01-package-manager-git-url.png)

7. After installation, the package should appear in the Package Manager.

![Package installed](../images/tutorial/01-package-installed.png)

### ...From Local Disk

For local development:

1. After clicking on **+** select **Add package from disk...**.
2. Select the package `package.json` file from your local package folder.

## Setup The Unity Scene

1. Open **GAMA > GAMA Panel**.
![Open the GAMA Panel menu](../images/tutorial/01-open-gama-panel-menu.gif)
3. Click **Setup Scene**. ![Setup Scene button](../images/tutorial/01-setup-scene-button.png)

![GAMA Panel opened](../images/tutorial/01-gama-panel-open.png)

4. After a quick build...
![Unity project ready](../images/tutorial/01-unity-project-ready.png)

...your scene should contain every object needed to communicate with the middleware.
You can verify that the scene contains:
   - a player or camera rig;
   - a `Connection Manager`;
   - a `Game Manager`;
   - required scene roots for preview and runtime objects.
![Scene ready for middleware](../images/tutorial/01-scene-ready-for-middleware.png)


## Result

At the end of this chapter, Unity is ready to communicate with the middleware.
