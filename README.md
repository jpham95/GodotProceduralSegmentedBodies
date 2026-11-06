# Procedural Segmented Body Generator For Godot 4.7 .NET

Build and pose segmented creatures in the Godot 4.7 editor using your own meshes. Define a torso, add limbs, and choose the segments that make up each limb. The tool measures the segment meshes, generates a skeleton and creates inverse kinematics (IK) targets so you can pose limbs by moving their endpoints.

To make the creature walk, assign a locomotion settings resource before generating. Grounded limbs will then take procedural steps according to the assigned settings as the body moves and turns.

## Quick start: generate an example using test assets

Use a supplied body definition and locomotion settings to try the generator and see an example before building your own creature.

1. Open the project in Godot 4.7 .NET and build the C# project.
2. Open a 3D scene and add a `ProceduralSegmentedBody` node.
3. **OPTIONAL:** Enable the project's gizmo plugin by navigating to `Project>Project Settings...`, click on the `Plugins` tab and toggling the `Enabled` boolean for `Segmented Body Gizmo Preview`.
4. Select the node. In the inspector, drag a supplied example body definition from the FileSystem dock into the `Body Definition` field. If you enabled the `Segmented Body Gizmo Preview` plugin in the previous step, you can view a preview of the selected body definition by toggling the `Show Preview` boolean on the node.
5. Click `Generate / Regenerate From Definition`. The creature should appear in the viewport with its own meshes, skeleton, and IK targets.
> **Regenerating replaces all children and bones of the ProceduralSegmentedBody node.** Keep all cameras, player controllers, and other nodes you want to preserve outside of the ProceduralSegmentedBody hierarchy. To make lasting changes, edit the body, limb and segment definitions or locomotion settings rather than the generated meshes.
6. In the Scene Tree, select a limb's `TargetNode` marker and move it with the move tool. The limb should bend toward the marker. This works with or without locomotion settings assigned.

Test animated locomotion using the supplied test level.

1. **OPTIONAL:** To remap movement controls, navigate to `Project>Project Settings...` and click on the `Input Map` tab and assign the movement actions to your desired keys.
2. Open the `procedural_segmented_body_test_level` scene.
3. Select the existing `ProceduralSegmentedBody` node under `PlayerController` and generate a creature based on your desired `Body Definition` with an associated `Locomotion Settings` resource.
4. Press the `Run Current Scene (F6)` to run the scene and walk your creature around using the assigned keys - grounded limbs should step according to `Locomotion Settings` assigned in the selected `Body Definition`.

## Generate a body

### Create your own body definition

A `Body Definition` describes a single creature in a saveable, shareable resource `.res` file. 

To create a `Body Definition` first and begin creating a creature
- Select the `ProceduralSegmentedBody` node.
- Open the dropdown beside `Body Definition`, and choose `New BodyDef`. 
- Click on the newly created `BodyDef` to expand it and see its controls.
- Right click the resource and select `Save` to save and share this resource between scenes or creatures.

| Body Definition Control | Description |
| --- | --- |
| `Head Mesh` | **Optional.** Adds a head mesh on top of the torso, connected by a single bone. Leave empty to generate a headless creature. |
| `Torso Mesh` | **Required.** Supplies the central body mesh. Its bounds determine the default head and limb attachment positions. |
| `Joint Mesh` | **Optional.** Places this mesh at the generated segment joints. Leaving it empty omits joint visuals - bones and IK still work without it. |
| `Limbs` | Required (at least 1 limb). An Array that holds one `Limb Definition` per limb. The next section [Add limbs](#add-limbs) explains how to set limbs up. |
| `Inverse Kinematics Type` | Selects the solver used by the creature's limb chains: [CCDIK](https://docs.godotengine.org/en/stable/classes/class_ccdik3d.html#class-ccdik3d), [FABRIK](https://docs.godotengine.org/en/stable/classes/class_fabrik3d.html#fabrik3d), or [Jacobian IK](https://docs.godotengine.org/en/stable/classes/class_jacobianik3d.html#class-jacobianik3d). CCDIK and Jacobian IK seem to work a bit better than FABRIK with limbs which use joint limitations. |
| `Head Attachment Offset` | Moves the head from the top centre of the torso in torso-local coordinates. |
| `Head Rotation Degrees` | Rotates the head mesh relative to its attachment point, in degrees, |
| `Locomotion Settings` | **Optional.** Assign a `Locomotion Settings` resource to generate a locomotion controller for grounded limbs to animate creature movement with steps. Leave empty to generate a creature you can pose manually using its IK targets. |

### Add limbs

Expand `Limbs` in the `Body Definition`, add an array element, and create a new `LimbDef` in that slot. Give it a unique `Limb ID`, such as `FrontLeftLeg`, then choose a `Preset`.

`Preset` provides an easy, streamlined way to define the limb's starting attachment point, and determines whether its endpoint uses ground placement. 
> For example to create two legs, give each one the `Leg` preset, then use opposite X values (e.g.: 0.5 and -0.5) in `Attachment Position` to separate them. Use `Attachment Rotation Degrees` to angle the legs outward without moving their attachment points.

| Limb Definition Control | Description |
|---|---|
| `Limb ID` | **Required.** Unique name for this limb within the body. Used to name its generated bones, meshes and IK target. |
| `Segments` | Required (at least 1 segment). An Array that holds one `Segment Definition` per segment, ordered from the torso outward. The next section [Build segment chains](#build-segment-chains) explains how to build this chain. |
| `Attachment Position` | Moves the limb root relative to its attachment point, in torso-local coordinates. |
| `Attachment Rotation Degrees` | Rotates the whole limb around its attachment point. Use this to set the starting angle of a leg or arm. |
| `Preset` | `Leg` attaches below the torso and enables ground placement. `Arm` attaches to one side without ground placement. `Custom` lets you define the attachment location and grounding behaviour. |
| `Use Magnet Targets` | **Optional.** For limbs with >2 segments. Adds an intermediate IK target that influences how the limb bends as it reaches its end target. This is achieved by adding a second, preceding setting to this limb's IK solver. Adjust before final usage - incorrect placement can have undesirable results. After generation move the end target through its intended range. if the limb folds back on itself, adjust the magnet marker and test again. |
| `Arm Attach Side` | **Arm preset only.** Attaches to the left or right side of the torso. |
| `Step Group` | For grounded limbs only when step groups are enabled in `Locomotion Settings`. Limbs in the same numbered group can begin steps together. Use a value from 1 to Step Group Count (defined in Locomotion Settings). |
| `Custom Limb Direction` | **Custom preset only.** Sets the attachment location expressed as multiples of the torso's half-size on each axis. E.g.: (0, -1, 0) attaches at the bottom-center. |
| `Custom Ground Foot` | **Custom preset only.** Enables ground placement for the limb. If locomotion settings are assigned, the limb can also take procedural steps. |

**NOTE:** Limb Definition also includes End Mesh settings discussed below in [Set up feet or hands](#set-up-feet-or-hands)

### Build segment chains

// how a segment is structured


// table of segment definition inputs -> what they do

### Set up feet or hands

// end mesh settings inputs -> what they do

## Procedural Animation

### Locomotion settings

// how locomotion settings is structures

// table of locomotion settings inputs -> what they do

### Adjust the generated stance

// Target/Rest position button usage

### Tune the walk

// value hints

## Asset Organisation

// basic file tree

## Requirements

// godot/.NET version requirements