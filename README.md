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

# Generate a body

## Create your own body definition

// how a body is structured

// table of inputs -> what they all do

## Add limbs

// how a limb is structured

// arm vs leg vs custom

// table of limb definition inputs -> what they do

## Build segment chains

// how a segment is structured

// table of segment definition inputs -> what they do

## Set up feet or hands

// end mesh settings inputs -> what they do

# Procedural Animation

## Locomotion settings

// how locomotion settings is structures

// table of locomotion settings inputs -> what they do

## Adjust the generated stance

// Target/Rest position button usage

## Tune the walk

// value hints

# Asset Organisation

// basic file tree

# Requirements

// godot/.NET version requirements