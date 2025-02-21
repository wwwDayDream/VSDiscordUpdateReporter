# Discord Mod Update Reporter
Reports mod updates to your Discord server via a Webhook Url by running `/check-mods` in server console/in-game.

![image](https://github.com/user-attachments/assets/f4767bf6-d82d-4718-9ff6-f2648de78b86)
![image](https://github.com/user-attachments/assets/c17efa55-976b-4568-93bb-54643dce7c6c)

Generate a Webhook URL in a (text) channel's Integrations settings and copy the URL. After launching the server you can link your url to either, or both, commands by entering the command with the link as the argument. Any subsequent calls, even after restart, will use the last used url for that command.

`/check-mods <WebhookUrl | Optional: Defaults to last used Webhook Url for this command>` will trigger the update report.

`/list-mods  <WebhookUrl | Optional: Defaults to last used Webhook Url for this command>` will list the mods as embeds in about 10 embeds per 1 message and 1 embed per mod.

### Support
Please reach out on Discord: [Daydreaming](https://discord.gg/Mherqbcmep), or create an issue on the repository here!

### Project Info
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) and all notable changes will be documented in the CHANGELOG.md file next to this, according to the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format. Works incorporating this should utilize the CHANGELOG file to indicate divergence from the source material (this work) in accordance with the LICENSE file next to this.
