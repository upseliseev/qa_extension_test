# qa_extension_test
An extension designed for QA to use for testing HUD against common extension situations 

Below is a list of the supported commands, as well as their effect: 
- reorder- select two random items in the list, and switch their positions. 


- update [index of item in the recycler view list]. 
	- Options: 
		- --selectable [isSelectable]- update whether or not the card is selectable 
		- --label [new label]- update the card's label  
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
				
		- --position [new position] - update the position field of the card 
		Example: to update the second card in a sequence to be selectable, the command would look like this: `update 1 --selectable true`.
	- The order of the options does not matter, as long as the values for each option are valid. 
		