[DOME] How the installer of an app "knows" what document types & latest versions the to-be-installed app
supports, so it can invoke the downgrade of the files using the still-installed version of the app
and tell it to what version the downgrade should be?


[DONE] The migrate-down command should be internally transactional.
Before modifying any data files, it should first create a temporary backup of the V2 data.
If any step of the downward migration fails, it must catch the exception,
restore the V2 data from its temporary backup, and then exit with a non-zero code.
This ensures that a failed migration leaves the V2 application in a consistent, restartable state.




