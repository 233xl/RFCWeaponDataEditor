# **Ravenfield Weapon Data Editor**

An editor that changes and saves .rfc file simply.

## v1.4: compression progress and object deletion

The status bar now shows separate writing, compression, verification, and finalization stages. LZ4 and LZMA report real 0–100% compression progress from the compression library; uncompressed output skips that stage. A save is reported complete only after verification and final file placement.

Select a Projectile or Weapon and choose **Delete object**, or press Delete while the component list/tree has focus. Deletion includes the selected prefab's owned components and child objects. Weapon registration entries and bundle preload ranges are cleaned up; shared textures and meshes remain intact. Changes are staged in memory until Save/Save As, and overwrite saves retain the previous-file backup.

Projectiles still used by weapons cannot be deleted: assign those weapons to another projectile first. Other live object references, or assets whose references cannot be inspected safely, also block deletion. Reset cannot restore a weapon reference to a deleted projectile. To discard unsaved deletions, reopen the file; to restore a saved version, use its backup.

## v1.3: clone projectiles and reassign weapons

The left panel now has a Projectile tree with connecting lines. Each projectile contains the weapons that use it. Select any node to edit its parameters.
Select a projectile and choose **Clone projectile** to name a copy and check which weapons should switch to it. **Assign weapons** changes only the checked weapons; unchecked weapons keep their current references. Choose a different destination projectile to move a weapon there.

Cloning duplicates the projectile GameObject, all its components, and its child hierarchy, assigns unique Path IDs, remaps internal references, and updates bundle preload ranges. Shared meshes, textures, and other referenced resources stay shared. The original projectile's numeric parameters remain independent from the copy.
Both Save and Save As support the additions, existing compression settings, backup, and payload verification. Resetting a weapon restores its most recently saved projectile reference as well as its parameter values. Resetting a new projectile resets its parameters; it does not remove the added object.

Only same-serialized-file assignments are supported. Missing, external, cyclic, or unsupported prefab hierarchies are rejected before committing a clone. Reopened output and a synthetic child hierarchy are tested; in-game behavior has not been verified.

Based on UABEA. Multi-language supported (English and Simplified Chinese).

## v1.2: compressed RFC saves

Save and Save As now rebuild the bundle, compress it, then verify every asset and resource stream before publishing the result.
The compression selector offers Auto, LZ4, LZMA, and Uncompressed. Auto preserves LZ4/LZMA inputs and uses LZ4 for uncompressed inputs, including files produced by older editor versions.
Compression can be changed and saved without editing a weapon parameter. Overwrite saves still create a verified-original backup.

LZ4 is generally a good local-play choice: small files with fast chunk decompression. LZMA usually produces smaller files but takes longer to save and requires whole-stream decompression to read assets. Uncompressed bundles skip decompression but read more disk data. Actual Ravenfield loading performance depends on its loader and the computer; no in-game timing has been measured.
Recompression is lossless for the asset payloads but does not guarantee the author's exact original file size.

Build: `dotnet publish src/RfcEditor.csproj -c Release --no-self-contained -o build`


