# qa_extension_test
An extension designed for QA to use for testing HUD against common extension situations

These are the steps required for installing the Extension and getting it up and running! 
	1. Install skytools, following the steps outlined here: https://developer.upskill.io/start/ 
	2. Once you install skytools, pull down this repo and save it where-ever you usually keep your Github repos. 
	3. Open the repo in Visual Studio Code as a folder. 
	4. The last step for set up is to get your api credentials. For generation of the credentials, follow the same steps as those in the developer portal. Once you have the JSON credentials, paste them into the `credentials.json` file found inside of the `qa_extension_test` folder. 
	5. Open a terminal in Visual Studio Code, navigate into `qa_extension_test`, and use `dotnet run` to run the program! Then, just follow the steps outlined in the terminal. 


Below is a list of the supported commands, as well as their effect: 
- reorder- select two random items in the list, and switch their positions. 


- update [index of item in the recycler view list]. 
	- Options: 
		- --selectable [isSelectable]- update whether or not the card is selectable 
		- --label [new label]- update the card's label. using quotation marks here
		- --subdued [isSubdued] - update whether or not the card should appear subdued 
		- --component [type] - update the card with the default version of the specified component 
			- available components: 
				- default
				- openSequence
				- calling 
				- completion
				- imageCapture
				- videoCapture
				- audioCapture
				- decision
				- scanning
				
		- --position [new position] - update the position field of the card 
		Example: to update the second card in a sequence to be selectable, the command would look like this: `update 1 --selectable true`.
	- The order of the options does not matter, as long as the values for each option are valid. 
		