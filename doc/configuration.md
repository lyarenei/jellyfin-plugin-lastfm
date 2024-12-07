# Plugin configuration

Here are documented all plugin settings and their effects.
There are two sections, the [user one](#user-configuration) and the [general](#general-configuration) one.

## User configuration

To use the plugin, you need to configure users on your server who wish to send data to ListenBrainz.
There are several things to configure:

##### ListenBrainz API token

In order to send data at all, you need to set up a ListenBrainz API token for user you want to send the data to.
This token can be found at [your ListenBrainz user profile](https://listenbrainz.org/profile/).
Simply paste it and you are good to go. You can also check/verify the token if you want to be extra sure.

Please keep in mind that the token is naturally accessible to the server admin as the admin is also the one who manages
the configuration. The API token is obfuscated in the plugin config file.

##### Enable submitting listens

Enables listen submitting for selected user. Pretty self-explanatory, nothing much to add here.

##### Enable syncing favorites

Enables marking `favorite` tracks in Jellyfin as `loved` listens in ListenBrainz and vice-versa. Marking listens as
'hated' is not supported as Jellyfin does not have such concept. If you unmark favorite track in Jellyfin, it will be
marked as neutral in ListenBrainz. Favorite albums and artists are not supported as these are not supported by ListenBrainz.

## General configuration

Options affecting the plugin behavior can be configured in this section.

##### ListenBrainz API URL

If you wish to use another instance of ListenBrainz, either a self-hosted one, or some other service with compatible
API, you can set a custom URL here.

##### MusicBrainz API URL

Same story as above, but for MusicBrainz.

##### Fetch additional metadata from MusicBrainz

Some data cannot be provided by Jellyfin alone for listen submission.
If enabled, the plugin will also pull metadata from MusicBrainz for specified track, which will be then included into
listen submission. This feature is completely optional and does not affect the core function of this plugin
(listen submission) in any way. For the lookup to work, the tracks needs to have a `Track MBID` set in their metadata.

Currently used metadata from MusicBrainz:

- **Recording MBID**

  This MBID is used to link the listen of specific track with MusicBrainz entry.
  Jellyfin currently does not support this, even if it's included in the track file metadata.
  The closest is the Track MBID, but these are not the same.
  If the fetching is not enabled, then, in ListenBrainz, you will see a plaintext track name in a listen, instead of a
  hyperlink to MusicBrainz entry.

- **Multiple artists credit**

  Recognizing multiple artists of a song is unfortunately a mess, as it's not standardized.
  However, this is only a part of the problem. Even if Jellyfin would recognize artists correctly, it still does not
  store join phrases to be able to reconstruct the correct artist credit string.
  As you can guess, MusicBrainz provides this information, so the plugin makes use of that.
  Please note that, usually, the default names will be used instead of alternative ones (for example transliterated
  asian names), so if you have customized artist names in your metadata, it will be ignored.
  If the fetching is not enabled, the plugin will default to sending all artist names, separated by a comma.

- **ISRCs**

  ISRC stands for `International Standard Recording Code` and is a code uniquely identifying a specific recording.
  There is not much to add here. Jellyfin does not store this code, so the only option is to ask MusicBrainz.
  In some cases, there may be multiple ISRCs stored in MusicBrainz for a specific recording. In these cases, the plugin
  simply chooses the first one.


Additionally, there are some plugin features which depend on this integration:

- Immediate favorite sync
  - Uses recording MBID

- Favorite sync (ListenBrainz -> Jellyfin)
  - Uses recording MBID


##### Use alternative event for recognizing listens

The plugin can work in two distinct modes of listen recognition.
There is the first one, which makes use of a `PlaybackStopped` event. This is the original and default mode.
The other one is a more recent addition and makes use of `UserDataSaved` event.
Each mode has its advantages and disadvantages, described below.

###### PlaybackStopped event mode

Using `PlaybackStopped` event, this mode has an advantage that the plugin can check ListenBrainz conditions for listen
submission (played 50% or 4 minutes). Of course, only if the playback position for that event is reported as well. One
big disadvantage is that this mode is suitable only for online playback - because your client needs to report to the
server that the playback has ended. Which is obviously not possible if you are offline.

Another disadvantage is that not many third party clients out there even report playback stops, leaving you with
unrecorded listens. But, as already mentioned earlier, even if the client reports stopped playback, the parameter of
playback position is optional. If this parameter is not set, then the event is ignored and no listen for that track
will be sent.

After `UserDataSaved` mode has been introduced, this is also the default mode to keep the plugin behavior consistent.

###### UserDataSaved event mode

This mode attempts to solve disadvantages of the `PlaybackStopped` mode by using event which is emitted when saving user
data - more specifically - when marking items in Jellyfin as played. For one, clients are more likely to at least mark
items as played. But, when marking items as played, the clients can also report when the item has been played. So, if
your client supports reporting playbacks during offline mode (once it's online again), it is possible to send listens
retroactively.

However, there are also some disadvantages.

The first one is that Jellyfin does not define when an item should be marked as played. Usually, one would think that
this means when the item has been fully played. But, technically, any item has been played if it has been played - no
matter the amount of playback time. This means that you can mark items as played just when the playback started
(this is actually what Jellyfin Web client does) and from ListenBrainz point of view, this would not be a valid
submission. The plugin tries to address this by internally tracking when and what has started playing and when an item
is marked as played, the plugin checks if at least a sufficient amount of time has passed from when the playback
started to qualify as a valid listen.

The second disadvantage is related to the first one. As mentioned, it is possible to specify when the item was marked as
played, however this field is optional. If the value is missing, the plugin would not be able to evaluate playback
conditions. In such cases, the plugin will default to current time. This should be fine in most cases, however, if the
client does not specify times when reporting playbacks retroactively, all listens reported in that time will have the
same timestamp.

##### Immediate favorite sync

Modifies the behavior of favorite sync feature. If enabled, the favorite status of a track is synced immediately to
ListenBrainz. Standard favorite sync (after playback of track finishes) is not affected. This feature only works if a
track has required metadata (track MBID) and the MusicBrainz integration is enabled.

##### Emit UpdateUserRating events

When syncing loved listens from ListenBrainz, the plugin does not use standard method to mark items as favorite - as that
would emit the `UpdateUserRating` events, which would feed into the immediate favorite sync feature - and that would
cause unnecessary favorite track updates back to ListenBrainz.

Enable this if you are using other plugins which work with favorite items. If enabled, the plugin will automatically
disable `Immediate favorite sync` feature (if enabled) during the sync to prevent the issue described above. This
feature only works if a track has required metadata (track MBID) and the MusicBrainz integration is enabled.

##### Backup of listens

Save listen locally before it is sent to ListenBrainz. To configure backups, first select the path where the backups
should be created. Then enable backups in user settings for each user you wish to create backups for.

If the path is not set, backups will **NOT* be created, even if there are users with backups enabled. The backup file
structure is: `<configured_path>/<listenbrainz_username>/yyyy-MM-dd.json` - each user has its own directory and each day
a new backup file is created. Currently, the JSON format should be the same or similar to exported listens from ListenBrainz.

##### Allowed libraries for listen submission

Depending on your setup, you may not want to record listens of audio from a certain library. Listens of audio from
unchecked libraries will be ignored and not submitted to ListenBrainz.

In case of an audio being in multiple libraries, listens for that audio will be sent if at least one of these libraries
is allowed.

By default, all non-music libraries are excluded.
