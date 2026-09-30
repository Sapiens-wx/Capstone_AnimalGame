// Run with fmodstudiocl -script against Anthony's existing authoring project.
(function () {
    "use strict";
    var expectedProject = "c:/users/pc/fmodprojects/capstone_animalgame/capstone_animalgame.fspro";
    var actualProject = String(studio.project.filePath).replace(/\\/g, "/").toLowerCase();
    if (actualProject !== expectedProject) {
        throw new Error("Headroom adjustment refuses to modify a different authoring project.");
    }
    var paths = ["Robot/Scan/Charge", "Robot/Scan/Pulse", "Robot/Camera/Open", "Robot/Camera/Close",
        "Robot/Camera/Move", "Robot/Camera/Focus", "Robot/Camera/Shutter"];
    // Materialize lazy-loaded event metadata before resolving object paths.
    var events = studio.project.model.Event.findInstances();
    events.forEach(function (event) { console.log("M1_EXISTING " + event.getPath() + " " + event.id + " valid=" + event.isValid); });
    var ids = paths.map(function (path) {
        var event = studio.project.lookup("event:/" + path);
        if (!event || !event.isValid) {
            throw new Error("Missing event " + path);
        }
        return event.id;
    });
    studio.project.workspace.mixer.masterBus.volume = -7;
    if (!studio.project.save()) {
        throw new Error("Saving the -7 dB master headroom failed.");
    }
    paths.forEach(function (path, index) {
        var event = studio.project.lookup("event:/" + path);
        if (event.id !== ids[index]) {
            throw new Error("Event GUID changed for " + path);
        }
        console.log("M1_HEADROOM event:/" + path + " " + event.id);
    });
    console.log("M1_HEADROOM Master volume = " + studio.project.workspace.mixer.masterBus.volume + " dB; all event GUIDs retained.");
}());
