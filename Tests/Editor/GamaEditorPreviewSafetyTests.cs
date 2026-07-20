using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GamaEditorPreviewSafetyTests
{
    private const string PrecisionJson =
        "{\"precision\":100,\"position\":[0,0],\"world\":[1000,1000],\"minPlayerUpdateDuration\":50}";

    private const string PropertiesJson =
        "{\"properties\":[{\"id\":\"trees\",\"tag\":\"trees\",\"hasPrefab\":true," +
        "\"prefab\":\"Trees/Tree\",\"size\":100,\"visible\":true}]}";

    private bool originalAskToSavePreference;
    private Scene originalActiveScene;
    private Scene testScene;
    private bool ownsIsolatedTestScene;
    private bool testStateInitialized;

    [SetUp]
    public void SetUp()
    {
        originalActiveScene = SceneManager.GetActiveScene();
        try
        {
            testScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            ownsIsolatedTestScene = true;
            SceneManager.SetActiveScene(testScene);
        }
        catch (InvalidOperationException)
        {
            // A brand-new batchmode project starts with an unsaved untitled scene,
            // for which Unity forbids additive scene creation. In batchmode it is
            // safe to replace that transient scene. An interactive user's populated
            // scene is never touched by the fallback.
            if (Application.isBatchMode)
            {
                testScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
                ownsIsolatedTestScene = false;
            }
            else if (!originalActiveScene.IsValid() ||
                     !originalActiveScene.isLoaded ||
                     originalActiveScene.GetRootGameObjects().Length != 0)
            {
                Assert.Ignore("Could not create an isolated scene without touching the open scene.");
            }
            else
            {
                testScene = originalActiveScene;
                ownsIsolatedTestScene = false;
            }
        }

        originalAskToSavePreference =
            GamaEditorPreviewSafetyPreferences.AskToSavePlayPreviewOnExit;
        testStateInitialized = true;
        GamaEditorPreviewSafetyPreferences.AskToSavePlayPreviewOnExit = true;
        GamaEditorPreviewSafetyDialogs.ResetTestHooks();
        GamaEditorPreviewSafetyDialogs.ModalAvailabilityOverride = () => true;
        GamaEditorPlayRuntimeRecorder.ResetForTests();
        GamaEditorPlayExitPreviewCapture.ClearPendingSnapshot();
        DestroyPreviewRoots();
    }

    [TearDown]
    public void TearDown()
    {
        if (testStateInitialized)
        {
            DestroyPreviewRoots();
            GamaEditorPlayExitPreviewCapture.ClearPendingSnapshot();
            GamaEditorPlayRuntimeRecorder.EndPlaySession();
            GamaEditorPreviewSafetyDialogs.ResetTestHooks();
            GamaEditorPreviewSafetyPreferences.AskToSavePlayPreviewOnExit =
                originalAskToSavePreference;
        }

        if (ownsIsolatedTestScene &&
            originalActiveScene.IsValid() &&
            originalActiveScene.isLoaded)
        {
            SceneManager.SetActiveScene(originalActiveScene);
        }

        if (ownsIsolatedTestScene && testScene.IsValid() && testScene.isLoaded)
        {
            EditorSceneManager.CloseScene(testScene, true);
        }
    }

    [Test]
    public void PlayExitSave_YesWithoutExistingPreview_IsApproved()
    {
        int saveDialogCalls = 0;
        int replacementDialogCalls = 0;
        GamaEditorPreviewSafetyDialogs.ComplexDialog = (_, _, _, _, _) =>
        {
            saveDialogCalls++;
            return 0;
        };
        GamaEditorPreviewSafetyDialogs.ConfirmDialog = (_, _, _, _) =>
        {
            replacementDialogCalls++;
            return true;
        };

        bool approved = GamaEditorPreviewSafety.TryApprovePlayExitSave(
            true,
            false);

        Assert.That(approved, Is.True);
        Assert.That(saveDialogCalls, Is.EqualTo(1));
        Assert.That(replacementDialogCalls, Is.Zero);
    }

    [Test]
    public void PlayExitSave_No_SkipsOnceWithoutReplacementPrompt()
    {
        int replacementDialogCalls = 0;
        GamaEditorPreviewSafetyDialogs.ComplexDialog = (_, _, _, _, _) => 1;
        GamaEditorPreviewSafetyDialogs.ConfirmDialog = (_, _, _, _) =>
        {
            replacementDialogCalls++;
            return true;
        };

        bool approved = GamaEditorPreviewSafety.TryApprovePlayExitSave(
            true,
            false);

        Assert.That(approved, Is.False);
        Assert.That(replacementDialogCalls, Is.Zero);
        Assert.That(
            GamaEditorPreviewSafetyPreferences.AskToSavePlayPreviewOnExit,
            Is.True);
    }

    [Test]
    public void PlayExitSave_Never_DisablesTheProjectPreferenceAndFuturePrompts()
    {
        int saveDialogCalls = 0;
        GamaEditorPreviewSafetyDialogs.ComplexDialog = (_, _, _, _, _) =>
        {
            saveDialogCalls++;
            return 2;
        };

        Assert.That(
            GamaEditorPreviewSafety.TryApprovePlayExitSave(true, false),
            Is.False);
        Assert.That(saveDialogCalls, Is.EqualTo(1));
        Assert.That(
            GamaEditorPreviewSafetyPreferences.AskToSavePlayPreviewOnExit,
            Is.False);

        Assert.That(
            GamaEditorPreviewSafety.TryApprovePlayExitSave(true, false),
            Is.False);
        Assert.That(saveDialogCalls, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PlayExitSave_WithExistingPreview_AsksSaveThenReplacement(bool replacePreview)
    {
        new GameObject(GamaEditorPreviewSafety.StaticPreviewRootName);
        List<string> dialogOrder = new List<string>();
        GamaEditorPreviewSafetyDialogs.ComplexDialog = (_, _, _, _, _) =>
        {
            dialogOrder.Add("save");
            return 0;
        };
        GamaEditorPreviewSafetyDialogs.ConfirmDialog = (_, _, _, _) =>
        {
            dialogOrder.Add("replace");
            return replacePreview;
        };

        bool saveApproved = GamaEditorPreviewSafety.TryApprovePlayExitSave(true, false);
        bool replacementApproved = saveApproved &&
                                   GamaEditorPreviewSafety.TryApproveCurrentReplacement(
                                       null,
                                       out _);

        Assert.That(replacementApproved, Is.EqualTo(replacePreview));
        Assert.That(dialogOrder, Is.EqualTo(new[] { "save", "replace" }));
    }

    [Test]
    public void PlayExitSave_WhenModalIsUnavailable_DoesNotInvokeDialogOrSave()
    {
        int dialogCalls = 0;
        GamaEditorPreviewSafetyDialogs.ModalAvailabilityOverride = () => false;
        GamaEditorPreviewSafetyDialogs.ComplexDialog = (_, _, _, _, _) =>
        {
            dialogCalls++;
            return 0;
        };

        bool approved = GamaEditorPreviewSafety.TryApprovePlayExitSave(
            true,
            false);

        Assert.That(approved, Is.False);
        Assert.That(dialogCalls, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public void PlayExitSave_WhenSnapshotIsMissingOrCorrespondingPreviewExists_DoesNotPrompt(
        bool hasCompleteSnapshot,
        bool correspondingPreviewWasLoaded)
    {
        int dialogCalls = 0;
        GamaEditorPreviewSafetyDialogs.ComplexDialog = (_, _, _, _, _) =>
        {
            dialogCalls++;
            return 0;
        };

        bool approved = GamaEditorPreviewSafety.TryApprovePlayExitSave(
            hasCompleteSnapshot,
            correspondingPreviewWasLoaded);

        Assert.That(approved, Is.False);
        Assert.That(dialogCalls, Is.Zero);
    }

    [Test]
    public void EditablePreviewDetection_FindsInactiveRootAndItsSession()
    {
        GameObject root = new GameObject(GamaEditorPreviewSafety.StaticPreviewRootName);
        GamaPreviewSession session = root.AddComponent<GamaPreviewSession>();
        root.SetActive(false);

        bool found = GamaEditorPreviewSafety.TryFindEditablePreview(
            out GameObject foundRoot,
            out GamaPreviewSession foundSession);

        Assert.That(found, Is.True);
        Assert.That(foundRoot, Is.SameAs(root));
        Assert.That(foundSession, Is.SameAs(session));
    }

    [Test]
    public void ReplacementApproval_ForAlreadyApprovedInactiveRoot_DoesNotPromptAgain()
    {
        GameObject root = new GameObject(GamaEditorPreviewSafety.StaticPreviewRootName);
        root.SetActive(false);
        int dialogCalls = 0;
        GamaEditorPreviewSafetyDialogs.ConfirmDialog = (_, _, _, _) =>
        {
            dialogCalls++;
            return false;
        };

        bool approved = GamaEditorPreviewSafety.TryApproveCurrentReplacement(
            root,
            out GameObject currentRoot);

        Assert.That(approved, Is.True);
        Assert.That(currentRoot, Is.SameAs(root));
        Assert.That(dialogCalls, Is.Zero);
    }

    [Test]
    public void ReplacementApproval_ForInactiveExistingRoot_UsesGenericPromptAndCanCancel()
    {
        GameObject root = new GameObject(GamaEditorPreviewSafety.StaticPreviewRootName);
        root.SetActive(false);
        int dialogCalls = 0;
        GamaEditorPreviewSafetyDialogs.ConfirmDialog = (_, _, _, _) =>
        {
            dialogCalls++;
            return false;
        };

        bool approved = GamaEditorPreviewSafety.TryApproveCurrentReplacement(
            null,
            out GameObject currentRoot);

        Assert.That(approved, Is.False);
        Assert.That(currentRoot, Is.SameAs(root));
        Assert.That(root == null, Is.False);
        Assert.That(dialogCalls, Is.EqualTo(1));
    }

    [Test]
    public void CommitReplacement_KeepsOldRootUntilACompletedRootExists()
    {
        GameObject existingRoot = new GameObject(
            GamaEditorPreviewSafety.StaticPreviewRootName);

        GamaEditorPreviewSafety.CommitReplacement(existingRoot, null);
        Assert.That(existingRoot == null, Is.False);

        GameObject completedRoot = new GameObject(
            GamaEditorPreviewSafety.BuildingPreviewRootName);
        Assert.That(existingRoot == null, Is.False);
        Assert.That(completedRoot == null, Is.False);

        GamaEditorPreviewSafety.CommitReplacement(existingRoot, completedRoot);

        Assert.That(existingRoot == null, Is.True);
        Assert.That(completedRoot == null, Is.False);
        Assert.That(
            completedRoot.name,
            Is.EqualTo(GamaEditorPreviewSafety.StaticPreviewRootName));
        Assert.That(
            GamaEditorPreviewSafety.TryFindEditablePreview(
                out GameObject currentRoot,
                out _),
            Is.True);
        Assert.That(currentRoot, Is.SameAs(completedRoot));
    }

    [Test]
    public void RuntimeRecorder_MergesMultipleWorldChunksIntoOneCompleteSnapshot()
    {
        const string firstChunk =
            "{\"names\":[\"tree-a\"],\"keepNames\":[\"tree-a\"]," +
            "\"propertyID\":[\"trees\"],\"pointsLoc\":[{\"c\":[10,20,0]}]," +
            "\"pointsGeom\":[],\"offsetYGeom\":[],\"attributes\":[{\"id\":\"tree-1\"}]," +
            "\"ranking\":[0]}";
        const string secondChunk =
            "{\"world\":{\"names\":[\"tree-b\"],\"keepNames\":[\"tree-b\"]," +
            "\"propertyID\":[\"trees\"],\"pointsLoc\":[{\"c\":[30,40,0]}]," +
            "\"pointsGeom\":[],\"offsetYGeom\":[],\"attributes\":[{\"id\":\"tree-2\"}]," +
            "\"ranking\":[1]}}";

        GamaEditorPlayRuntimeRecorder.RecordServerMessage("precision", PrecisionJson);
        GamaEditorPlayRuntimeRecorder.RecordServerMessage("properties", PropertiesJson);
        GamaEditorPlayRuntimeRecorder.RecordServerMessage("pointsLoc", firstChunk);
        GamaEditorPlayRuntimeRecorder.RecordServerMessage("world", secondChunk);

        Assert.That(GamaEditorPlayRuntimeRecorder.HasCompleteSnapshot, Is.True);
        Assert.That(
            GamaEditorPlayRuntimeRecorder.TryGetSnapshot(
                out GamaEditorPlayRuntimeSnapshot snapshot),
            Is.True);
        Assert.That(snapshot.PrecisionJson, Does.Contain("\"precision\":100"));
        Assert.That(snapshot.PropertiesJson, Does.Contain("\"id\":\"trees\""));

        WorldJSONInfo world = WorldJSONInfo.CreateFromJSON(snapshot.WorldJson);
        Assert.That(world, Is.Not.Null);
        Assert.That(world.names, Is.EqualTo(new[] { "tree-a", "tree-b" }));
        Assert.That(world.propertyID, Is.EqualTo(new[] { "trees", "trees" }));
        Assert.That(world.pointsLoc, Has.Count.EqualTo(2));
        Assert.That(world.attributes, Has.Count.EqualTo(2));
    }

    [Test]
    public void PendingSnapshot_StoreAndClearTracksAllTemporaryFiles()
    {
        const string worldJson =
            "{\"names\":[\"tree-a\"],\"keepNames\":[\"tree-a\"]," +
            "\"propertyID\":[\"trees\"],\"pointsLoc\":[{\"c\":[10,20,0]}]," +
            "\"pointsGeom\":[],\"offsetYGeom\":[],\"attributes\":[{}],\"ranking\":[0]}";
        GamaEditorPlayRuntimeSnapshot snapshot = new GamaEditorPlayRuntimeSnapshot(
            PrecisionJson,
            PropertiesJson,
            worldJson);
        GamaEditorPlayPreviewIdentity identity = new GamaEditorPlayPreviewIdentity(
            true,
            string.Empty,
            string.Empty,
            "experiment-42",
            "player-7",
            8001,
            8080);

        bool stored = GamaEditorPlayExitPreviewCapture.TryStorePendingSnapshot(
            snapshot,
            identity,
            out string error);

        Assert.That(stored, Is.True, error);
        Assert.That(error, Is.Null);
        Assert.That(GamaEditorPlayExitPreviewCapture.HasPendingSnapshot, Is.True);

        GamaEditorPlayExitPreviewCapture.ClearPendingSnapshot();

        Assert.That(GamaEditorPlayExitPreviewCapture.HasPendingSnapshot, Is.False);
    }

    [Test]
    public void PendingSnapshot_RestoreRefusalKeepsTheActualExistingPreview()
    {
        const string worldJson =
            "{\"names\":[\"tree-a\"],\"keepNames\":[\"tree-a\"]," +
            "\"propertyID\":[\"trees\"],\"pointsLoc\":[{\"c\":[10,20,0]}]," +
            "\"pointsGeom\":[],\"offsetYGeom\":[],\"attributes\":[{}],\"ranking\":[0]}";
        GamaEditorPlayRuntimeSnapshot snapshot = new GamaEditorPlayRuntimeSnapshot(
            PrecisionJson,
            PropertiesJson,
            worldJson);
        GamaEditorPlayPreviewIdentity identity = new GamaEditorPlayPreviewIdentity(
            true,
            string.Empty,
            string.Empty,
            "experiment-42",
            "player-7",
            8001,
            8080);
        Assert.That(
            GamaEditorPlayExitPreviewCapture.TryStorePendingSnapshot(
                snapshot,
                identity,
                out string storeError),
            Is.True,
            storeError);

        GameObject existingRoot = new GameObject(
            GamaEditorPreviewSafety.StaticPreviewRootName);
        int replacementDialogCalls = 0;
        GamaEditorPreviewSafetyDialogs.ConfirmDialog = (_, _, _, _) =>
        {
            replacementDialogCalls++;
            return false;
        };

        bool restored = GamaEditorPlayExitPreviewCapture.TryRestorePendingSnapshot(
            out string restoreError);

        Assert.That(restored, Is.False);
        Assert.That(restoreError, Is.Null);
        Assert.That(replacementDialogCalls, Is.EqualTo(1));
        Assert.That(existingRoot == null, Is.False);
        Assert.That(
            GamaEditorPreviewSafety.TryFindEditablePreview(out GameObject currentRoot, out _),
            Is.True);
        Assert.That(currentRoot, Is.SameAs(existingRoot));
        Assert.That(GamaEditorPlayExitPreviewCapture.HasPendingSnapshot, Is.False);
    }

    private static void DestroyPreviewRoots()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            return;
        }

        GameObject[] roots = activeScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null ||
                (root.name != GamaEditorPreviewSafety.StaticPreviewRootName &&
                 root.name != GamaEditorPreviewSafety.BuildingPreviewRootName))
            {
                continue;
            }

            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
