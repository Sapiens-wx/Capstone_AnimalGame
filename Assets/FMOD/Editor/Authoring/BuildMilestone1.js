/*
 * Run once with FMOD Studio 2.03.14's fmodstudiocl -script on a dedicated
 * copy of the bundled Examples project, stored outside the Unity repository.
 * The example content is replaced with Anthony's Milestone 1 player sounds.
 * This script saves the project. Run the CLI -diagnostic and -build commands
 * separately afterwards so their exit status can be checked independently.
 * After first initialization, run SetScanSustainLoop.js to author the 10 ms
 * sustain crossfade. Existing events and their fades are retained on re-run.
 */
(function () {
    "use strict";

    var repository = "C:/Users/PC/Capstone_AnimalGame";
    var sourceRoot = repository + "/FMOD/SourceAudio/Milestone1/";
    var exampleWorkspaceId = "{eca33bb7-c1b3-4fe2-9fef-b7789ec3bde5}";
    var workspace = studio.project.workspace;
    var projectPath = String(studio.project.filePath).replace(/\\/g, "/");
    var definitions = [
        { path: "event:/Robot/Scan/Charge", file: "Scan/Scan_Charge.wav", loopStart: 0.79, loopEnd: 0.88 },
        { path: "event:/Robot/Scan/Pulse", file: "Scan/Scan_Pulse.wav" },
        { path: "event:/Robot/Camera/Open", file: "Camera/Camera_Open.wav" },
        { path: "event:/Robot/Camera/Close", file: "Camera/Camera_Close.wav" },
        { path: "event:/Robot/Camera/Move", file: "Camera/Camera_Move_Loop.wav", loopWholeFile: true },
        { path: "event:/Robot/Camera/Focus", file: "Camera/Camera_Focus_Charge.wav" },
        { path: "event:/Robot/Camera/Shutter", file: "Camera/Camera_Shutter.wav" }
    ];

    function requireCondition(condition, message) {
        if (!condition) {
            throw new Error("Milestone 1 authoring: " + message);
        }
    }

    function instances(entityName) {
        var entity = studio.project.model[entityName];
        return entity ? entity.findInstances() : [];
    }

    function deleteInstances(entityName, keep) {
        // Re-query after every deletion because removing a parent may also
        // remove other objects from the initial result array.
        var removed = 0;
        while (true) {
            var objects = instances(entityName);
            var target = null;
            for (var i = 0; i < objects.length; ++i) {
                if (objects[i].isOfExactType(entityName) && (!keep || !keep(objects[i]))) {
                    target = objects[i];
                    break;
                }
            }
            if (!target) {
                break;
            }
            var targetId = target.id;
            requireCondition(studio.project.deleteObject(target), "Cannot delete " + entityName + " " + targetId);
            ++removed;
            requireCondition(removed < 10000, "Unexpected deletion count for " + entityName);
        }
        console.log("M1_CLEAN " + entityName + " " + removed);
    }

    function folder(name, parent) {
        var result = studio.project.create("EventFolder");
        result.name = name;
        result.folder = parent;
        return result;
    }

    function validateAndLogEvents() {
        for (var i = 0; i < definitions.length; ++i) {
            var definition = definitions[i];
            var event = studio.project.lookup(definition.path);
            requireCondition(event && event.isValid, "Missing or invalid event " + definition.path);
            var expectedOneShot = definition.loopStart === undefined && !definition.loopWholeFile;
            requireCondition(event.isOneShot() === expectedOneShot, "Unexpected one-shot/loop state for " + definition.path);
            requireCondition(event.banks.length === 1 && event.banks[0].name === "RobotTools", "Unexpected bank for " + definition.path);
            console.log("M1_EVENT " + JSON.stringify({ path: definition.path, guid: event.id, oneShot: event.isOneShot() }));
        }
    }

    requireCondition(studio.version.productVersion === 2 && studio.version.majorVersion === 3,
        "Requires the FMOD Studio 2.03 authoring API.");
    requireCondition(projectPath.toLowerCase().indexOf(repository.toLowerCase() + "/") !== 0,
        "The Studio source project must be outside the Unity repository.");
    requireCondition(projectPath.toLowerCase().slice(-6) === ".fspro", "No saved Studio project is loaded.");

    // Existing project metadata is lazy-loaded by the command-line tool.
    // Materialize events first so lookup can detect a previous successful run.
    studio.project.model.Event.findInstances();
    var existingCount = 0;
    for (var existingIndex = 0; existingIndex < definitions.length; ++existingIndex) {
        if (studio.project.lookup(definitions[existingIndex].path)) {
            ++existingCount;
        }
    }
    if (existingCount === definitions.length) {
        // Re-running the initialization command must not replace GUIDs or
        // overwrite subsequent work in Anthony's authoring project.
        validateAndLogEvents();
        requireCondition(studio.project.save(), "Saving the existing project failed.");
        console.log("M1_READY Existing events retained; build with fmodstudiocl -build -platforms Desktop.");
        return;
    }
    requireCondition(existingCount === 0, "A partial Milestone 1 setup exists; refusing to overwrite it.");
    requireCondition(String(workspace.id).toLowerCase() === exampleWorkspaceId,
        "This initializer only clears a dedicated copy of the bundled Examples project.");

    // Validate source paths before discarding any example content.
    for (var fileIndex = 0; fileIndex < definitions.length; ++fileIndex) {
        var sourceFile = studio.system.getFile(sourceRoot + definitions[fileIndex].file);
        requireCondition(sourceFile.exists(), "Missing source file " + definitions[fileIndex].file);
    }

    deleteInstances("Snapshot");
    deleteInstances("SnapshotGroup");
    deleteInstances("Event");
    deleteInstances("EventFolder");
    deleteInstances("MixerVCA");
    deleteInstances("MixerGroup");
    deleteInstances("MixerReturn");
    deleteInstances("MixerInput");
    deleteInstances("Bank", function (bank) { return bank.isMasterBank; });
    deleteInstances("AudioTable");
    deleteInstances("EffectPreset");
    deleteInstances("ParameterPreset");
    deleteInstances("ParameterPresetFolder");
    deleteInstances("Tag");
    deleteInstances("AudioFile");
    deleteInstances("DataFile");
    deleteInstances("EncodableAsset");

    var desktopPlatforms = instances("Platform").filter(function (platform) { return platform.name === "Desktop"; });
    requireCondition(desktopPlatforms.length === 1, "Expected exactly one Desktop platform in the seed project.");
    var masterBanks = instances("Bank").filter(function (bank) { return bank.isMasterBank; });
    requireCondition(masterBanks.length === 1, "Expected exactly one preserved master bank.");
    masterBanks[0].name = "Master";
    // Offline renders require this headroom for overlapping full-level pulses.
    workspace.mixer.masterBus.volume = -7;

    var robotFolder = folder("Robot", workspace.masterEventFolder);
    var scanFolder = folder("Scan", robotFolder);
    var cameraFolder = folder("Camera", robotFolder);
    var toolsBank = studio.project.create("Bank");
    toolsBank.name = "RobotTools";
    toolsBank.folder = workspace.masterBankFolder;

    for (var definitionIndex = 0; definitionIndex < definitions.length; ++definitionIndex) {
        var item = definitions[definitionIndex];
        var asset = studio.project.importAudioFile(sourceRoot + item.file);
        requireCondition(asset && asset.isValid && asset.length > 0, "Import failed for " + item.file);

        var name = item.path.substring(item.path.lastIndexOf("/") + 1);
        var newEvent = workspace.addEvent(name, false);
        newEvent.folder = item.path.indexOf("/Scan/") !== -1 ? scanFolder : cameraFolder;
        newEvent.relationships.banks.add(toolsBank);
        var track = newEvent.addGroupTrack("Audio");
        var sound = track.addSound(newEvent.timeline, "SingleSound", 0, asset.length);
        sound.audioFile = asset;
        sound.looping = false;

        if (item.loopWholeFile || item.loopStart !== undefined) {
            var loopStart = item.loopWholeFile ? 0 : item.loopStart;
            var loopEnd = item.loopWholeFile ? asset.length : item.loopEnd;
            requireCondition(loopEnd > loopStart && loopEnd <= asset.length,
                "The loop region exceeds " + item.file + " (" + asset.length + " s).");
            var markerTrack = newEvent.addMarkerTrack();
            markerTrack.addRegion(loopStart, loopEnd - loopStart, "Loop", studio.project.regionLoopMode.Looping);
        }

        console.log("M1_SOURCE " + JSON.stringify({ path: item.path, file: item.file, seconds: asset.length }));
    }

    validateAndLogEvents();
    requireCondition(studio.project.save(), "Saving the initialized project failed.");
    console.log("M1_READY " + projectPath + "; run -diagnostic, then -build -platforms Desktop -export-guids.");
}());
