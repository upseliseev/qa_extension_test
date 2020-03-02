using System.Net.Http.Headers;
using System.IO;
using System;
using Skylight.Client;
using System.Threading.Tasks;
using Skylight.Sdk;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Disconnecting;
using Skylight.Mqtt;
using System.Threading;
using Skylight.Api.Assignments.V1.Models;
using Skylight.Api.Authentication.V1.Models;
using System.Collections.Generic;

namespace ExtensionTest
{

    /*
        The goal of this program is to allow the testing of common extension behaviors without having to set up your own extension. 
    */

    class Extension
    {
        public static Manager skyManager;
        public static string userId; // id the user we are currently working with 
        private readonly static string FILES_DIRECTORY = Path.Join(".", "tmp"); // to be used for downloaded files 
        private static int AssignmentCount = 0;
        private static Assignment assign; 

        static async Task Main(string[] args)
        {
            try
            {
                //Create our manager and point it to our credentials file
                //We leave the parameter blank, so that it looks for the `credentials.json` in the root directory.
                skyManager = new Manager();
            }
            catch { return; }

            //0. Subscribe to Skylight event 
            await SubscribeToSkylightEvents();

            //1. get the user id for the user we want to test with 
            userId = await getUserId(); 
            if (userId == null)
            {
                Console.WriteLine("Exiting Skylight Hello World extension.");
                return;
            }

            //2. Remove all assignments
            await removeAllAssignmentsForUser(userId);

            //3. Create a new assignment
            //It is important to note that this next call doesn't reach out to the servers -- the assignment information is created in one fell swoop in memory first.
            var assignmentBody = createAssignment();

            //4. Assign the assignment to the user
            await assignToUser(assignmentBody, userId);

            //Let the user know that we've completed our work here and that they should continue on a device.
            Console.WriteLine("Congratulations! Your user is now set up with your assignment. Log in to the domain `" + skyManager.Domain + "` using that username and password in Skylight Web to view this assignment.");
            Console.WriteLine("Enter a command: "); // tell the user they can enter a command now 
            // TODO listen for input forever, or at least until the program is stopped 
            while(true) {
                string command = Console.ReadLine();
                Console.WriteLine("Got command: " + command); 
                await processCommand(command);
            }
        }

        private static async Task processCommand(string command) {
            // if the command is reorder, then there are no other parameters. 
            // we should randomly switch two items in the root sequence, and update the assignment accordingly 
            if(command.Equals("reorder")) {
                await reorderAndPatchAssignment(); 
            } else if (command.Contains("update")) {
                await updateAndPatchCard(command); 
            } else {
                Console.WriteLine("Got unknown command " + command +". Please try again with a valid command."); 
            }
        }

        /* switches positions of two random items in the root sequence of our current assignment, and patches the two card changes to 
           the backend so we immediately see the changes 
         */ 
        private static async Task reorderAndPatchAssignment() {
            var getSeqReq = new Skylight.Api.Assignments.V1.SequenceRequests.GetSequenceRequest(assign.Id, assign.RootSequence); 
            var seqResult = await skyManager.ApiClient.ExecuteRequestAsync(getSeqReq);
            Sequence rootSeq = seqResult.Content; 
            var cardsReq = new Skylight.Api.Assignments.V1.CardRequests.GetSequenceCardsRequest(assign.Id, assign.RootSequence); 
            var cardsResult =  await skyManager.ApiClient.ExecuteRequestAsync(cardsReq);  
            List<Card> cards = cardsResult.Content; 
            // sort by position so we can just directly modify the cards we need 
            cards.Sort(delegate(Card a, Card b){
                if(a.Position == null && b.Position == null) return 0; 
                else if (a.Position == null) return -1;
                else if(b.Position == null) return 1; 
                else if(a.Position < b.Position) return -1; 
                else if (a.Position > b.Position) return 1;
                else return 0; 
            });

            Random random = new Random(); 
            Int32 index1;
            Int32 index2;
            // looped to make sure we don't get the same value for both indices, since such a switch is not informative 
            // and does not benefit anyone, and is mostly a test of this extension than of the client 
            do {
                index1 = random.Next(cards.Count); 
                index2 = random.Next(cards.Count); 
            } while (index1 == index2); 

            // prints out the positions of the cards, as they would appear in the footer on HUD. 
            Console.WriteLine("Switching cards at positions " + index1 + " and " + index2); 

            // do the switcharoo 
            decimal? card1Pos = cards[index1].Position; 
            decimal? card2Pos = cards[index2].Position; 
            //flip the cards actual position fields, since that's what should determine what the order is client-side. 
            // if that doesnt happen then that is a client-side bug
            cards[index1].Position = card2Pos;
            cards[index2].Position = card1Pos; 

            // now we want to patch the changes to the new cards
            Dictionary<string, object> card1Changes = new Dictionary<string, object>(); 
            card1Changes.Add("position", cards[index1].Position); 
            var patchCard1Req = new Skylight.Api.Assignments.V1.CardRequests.PatchCardRequest(card1Changes, cards[index1].AssignmentId, cards[index1].SequenceId, cards[index1].Id); 

            Dictionary<string, object> card2Changes = new Dictionary<string, object>(); 
            card2Changes.Add("position", cards[index2].Position); 
            var patchCard2Req = new Skylight.Api.Assignments.V1.CardRequests.PatchCardRequest(card2Changes, cards[index2].AssignmentId, cards[index2].SequenceId, cards[index2].Id); 

            await skyManager.ApiClient.ExecuteRequestAsync(patchCard1Req); 
            await skyManager.ApiClient.ExecuteRequestAsync(patchCard2Req); 
        }

        private static async Task updateAndPatchCard(string command) {
            string[] commandInfoList = command.Split("--"); // split based on the options   

            string[] cardToUpdateData = commandInfoList[0].Split(" "); 
            if(cardToUpdateData.Length < 2 || cardToUpdateData.Length > 3) {
                Console.Error.WriteLine("Did not get correctly formatted required update information. Please verify your command and try again."); 
                return; 
            }
            if(commandInfoList.Length < 2) {
                Console.Error.WriteLine("No options specified. Please try again with at least one option"); 
                return; 
            }
            Int32 cardPos = Int32.Parse(cardToUpdateData[1]); 

            var getSeqReq = new Skylight.Api.Assignments.V1.SequenceRequests.GetSequenceRequest(assign.Id, assign.RootSequence); 
            var seqResult = await skyManager.ApiClient.ExecuteRequestAsync(getSeqReq);
            Sequence rootSeq = seqResult.Content; 
            var cardsReq = new Skylight.Api.Assignments.V1.CardRequests.GetSequenceCardsRequest(assign.Id, assign.RootSequence); 
            var cardsResult =  await skyManager.ApiClient.ExecuteRequestAsync(cardsReq);  
            List<Card> cards = cardsResult.Content; 
            Card card = cards[cardPos];

            Dictionary<string, object> cardChanges = new Dictionary<string, object>(); 
                    
            // go through the provided options and make the appropriate changes 
            for (int i= 1; i < commandInfoList.Length; i++) {
                Console.WriteLine("Processing command: " + commandInfoList[i]); 
                String[] optionDetails = commandInfoList[i].Split(" "); 

                if(optionDetails.Length < 2) {
                    Console.Error.WriteLine("Missing information in at least one option"); 
                }

                string option = optionDetails[0];
                string newValue = optionDetails[1]; 
            
                if(option.Equals("selectable")) {
                    Boolean newBool = false; 
                    if(Boolean.TryParse(newValue, out newBool)) {
                        cardChanges["selectable"] = newBool; 
                    } else {
                        Console.WriteLine("Invalid new value for option selectable"); 
                        return; 
                    }
                } else if (option.Equals("label")) {
                    string actualString = commandInfoList[i].Split(option)[1]; 
                    cardChanges["label"] = actualString.Trim(); 
                } else if (option.Equals("subdued")) {
                    Boolean newBool = false; 
                    if(Boolean.TryParse(newValue, out newBool)) {
                        cardChanges["subdued"] = newBool; 
                    } else {
                        Console.WriteLine("Invalid new value for option subdued"); 
                        return; 
                    }
                } else if (option.Equals("position")) {
                    Decimal newPos = 0; 
                    if(Decimal.TryParse(newValue, out newPos)) {
                        cardChanges["position"] = newPos; 
                    } else {
                        Console.WriteLine("Invalid new value for option position"); 
                        return; 
                    }
                } else if (option.Equals("component")) {
                    Component newComp; 
                    if(newValue.Equals("openSequence")) {
                        newComp = new ComponentOpenSequence(); 
                    } else if (newValue.Equals("default")) {
                        newComp = new ComponentDefault(); 
                    } else if (newValue.Equals("calling")) {
                        newComp = new ComponentCalling(); 
                    } else if (newValue.Equals("completion")) {
                        newComp = new ComponentCompletion(); 
                    } else if (newValue.Equals("imageCapture")) {
                        newComp = new ComponentCapturePhoto(); 
                    } else if (newValue.Equals("videoCapture")) {
                        newComp = new ComponentCaptureVideo(); 
                    } else if(newValue.Equals("audioCapture")) {
                        newComp = new ComponentCaptureAudio(); 
                    } else if (newValue.Equals("decision")) {
                                    // create the choice objects 
                        Choice choice1 = new Choice(); 
                        choice1.Label = "option 1";
                        choice1.Position = 1;
                        Choice choice2 = new Choice(); 
                        choice2.Label = "option 2"; 
                        choice2.Position = 2; 
                        Choice choice3 = new Choice();
                        choice3.Label = "option 3"; 
                        choice3.Position = 3; 
                        Dictionary<String, Choice> choices = new Dictionary<String,Choice>(); 
                        choices.Add("id1", choice1);
                        choices.Add("id2", choice2); 
                        choices.Add("id3", choice3); 
                        ComponentDecision component = new ComponentDecision(); 
                        component.Choices = choices; 
                        newComp = component;  
                    } else if (newValue.Equals("scanning")) {
                        newComp = new ComponentScanning(); 
                    } else {
                        Console.Error.WriteLine("Tried to add an invalid component type. Please try again."); 
                        return; 
                    }
                    cardChanges["component"] = newComp;  
                }
            }

            var patchCardReq = new Skylight.Api.Assignments.V1.CardRequests.PatchCardRequest(cardChanges, card.AssignmentId, card.SequenceId, card.Id);    
            await skyManager.ApiClient.ExecuteRequestAsync(patchCardReq); 
        }

        /*
            private helpers designed to encapsulate specific actions.
            TODO- subscribe to skylight events  
        */
        //In this method, we subscribe to Skylight events. In particular for this example, we're most interested in listening for the `Mark Complete` event.
        private static async Task SubscribeToSkylightEvents()
        {
            skyManager.MessagingClient.Connected += (object sender, MqttClientConnectedEventArgs args) =>
            {
                Console.WriteLine("Skylight MQTT client connected.");
            };

            skyManager.MessagingClient.Disconnected += (object sender, MqttClientDisconnectedEventArgs args) =>
            {
                Console.WriteLine("Skylight MQTT client disconnected.");
                Console.WriteLine(args.Exception.Message);
            };

            skyManager.MessagingClient.TopicSubscribed += (object sender, MqttMsgSubscribedEventArgs args) =>
            {
                Console.WriteLine("Skylight MQTT client subscribed to: " + args.Topic);
            };

            skyManager.MessagingClient.MessageReceived += (object sender, MessageReceivedEventArgs args) =>
            {
                //Console.WriteLine("Skylight Message received on topic " + args.Topic + " " + args.Message); //Uncomment this for more verbose logging of Skylight event messages
            };

            skyManager.MessagingClient.CardUpdated += async (object sender, CardUpdatedEventArgs args) => { await onCardUpdated(sender, args); };

            await skyManager.StartListening();
        }

        //@skydocs.start(mqtt.cardupdated.cardtags)
        //@skydocs.start(media.download)
        static async Task onCardUpdated(object sender, CardUpdatedEventArgs args)
        {
            /*
                There are many ways we can handle this event. We could:
                - use the cardId to determine what action to take
                - pull down the card and:
                    - look at its tags to determine what to do
                    - look at the card type and decide what to do

                For this example, we'll pull down the card and look at its tags (specifically, we'll make sure it has the MARK_COMPLETE_TAG tag that we used earlier)
            */
            var result = await skyManager.ApiClient.ExecuteRequestAsync(new Skylight.Api.Assignments.V1.CardRequests.GetCardRequest(args.AssignmentId, args.SequenceId, args.CardId));

            //Handle the resulting status code appropriately
            switch (result.StatusCode)
            {
                case System.Net.HttpStatusCode.Forbidden:
                    Console.Error.WriteLine("Error getting card: Permission forbidden.");
                    throw new Exception("Error getting card.");
                case System.Net.HttpStatusCode.Unauthorized:
                    Console.Error.WriteLine("Error getting card: Method call was unauthenticated.");
                    throw new Exception("Error getting card.");
                case System.Net.HttpStatusCode.NotFound:
                    Console.Error.WriteLine("Error retrieving card: User not found.");
                    throw new Exception("Error getting card.");
                case System.Net.HttpStatusCode.OK:
                    Console.WriteLine("Successfully retrieved card.");
                    break;
                default:
                    Console.Error.WriteLine("Unhandled card retrieval status code: " + result.StatusCode);
                    throw new Exception("Error getting card.");
            }

            var cardInfo = result.Content;
            // TODO describe what should be done based on the card state. for now, just print out which card was updated 
            Console.WriteLine("Update card: " + cardInfo.AssignmentId + "/" + cardInfo.SequenceId + "/" + cardInfo.Id);
        }
        //@skydocs.end()

        //@skydocs.end()

        /*
            This method will prompt for a username to use for this Hello World and then:
                0. If the user exists, ask if we want to continue the Hello World with that user
                1. Otherwise, prompt for a password and create the user
        */
        private static async Task<string> getUserId()
        {

            Console.WriteLine("Welcome to the Skylight Test extension! Please enter a username to use for this extension:");
            string username = Console.ReadLine();

            string userId = await getUserIdForUsername(username);

            //If userId isn't null, then that user already exists -- see if we actually want to use that user
            if (userId != null)
            {
                Console.WriteLine("That user currently exists in your domain. Would you like to continue this extension with that user? [type 'yes' or 'no']\n(IMPORTANT: This extension will delete all assignments from this user.)");
                string choice = Console.ReadLine();

                //Make sure the user explicitly specifies 'yes' or 'no'
                while (!(choice.ToLower().Equals("no") || choice.ToLower().Equals("yes")))
                {
                    Console.WriteLine("Please type 'yes' or 'no'.");
                    choice = Console.ReadLine();
                }
                if (choice.ToLower().Equals("no")) return null;
                return userId;
            }

            //Otherwise, prompt for a password and create the user
            string password = getPassword();
            await createUser("Extension", "Test", Role.User, username, password);
            userId = await getUserIdForUsername(username);

            //At this point, if userId is still null, we've thrown an exception.
            return userId;
        }

         //As the name suggests, this method will get the user ID for a given username -- or will return null, if the user doesn't exist
        private static async Task<string> getUserIdForUsername(string username) {
            //Create an API request for retrieving all users
            var getUsersRequest = new Skylight.Api.Authentication.V1.UsersRequests.GetUsersRequest();

            //Execute the API request
            var result = await skyManager.ApiClient.ExecuteRequestAsync(getUsersRequest);

            //Handle the resulting status code appropriately
            switch(result.StatusCode) {
                case System.Net.HttpStatusCode.Forbidden:
                    Console.Error.WriteLine("Error getting users: Permission forbidden.");
                    throw new Exception("Error getting users.");
                case System.Net.HttpStatusCode.Unauthorized:
                    Console.Error.WriteLine("Error getting users: Method call was unauthenticated.");
                    throw new Exception("Error getting users.");
                case System.Net.HttpStatusCode.OK:
                    Console.WriteLine("Successfully retrieved users.");
                    break;
                default:
                    Console.Error.WriteLine("Unhandled users list retrieval status code: " + result.StatusCode);
                    throw new Exception("Error getting users.");
            }
            
            //The users will be stored as a list in the result's Content, so we can iterate through them
            foreach(var user in result.Content) {
                if(user.Username == username)return user.Id;
            }

            return null;
        }

        //This code was pulled in from: https://social.msdn.microsoft.com/Forums/vstudio/en-US/455eefeb-7624-4d81-b921-30f19891b2a7/any-way-to-prompt-user-for-a-password-and-then-hide-it?forum=csharpgeneral
        private static string getPassword() { 
            Console.WriteLine(); 
            Console.Write("Enter desired password for this user: "); 

            string password = string.Empty; 

            ConsoleKeyInfo keyInfo = Console.ReadKey(true); 
            while (keyInfo.Key != ConsoleKey.Enter) { 
                Console.Write("*"); 
                password += keyInfo.KeyChar; 
                keyInfo = Console.ReadKey(true); 
            } 
            Console.Write("\n");

            return password; 
        } 
        static async Task createUser(string first, string last, Role role, string username, string password) {
            //This is the body of information we use to create a new user
            var newUserBody = new Skylight.Api.Authentication.V1.Models.UserNew
            {
                FirstName = first,
                LastName = last,
                Role = role,            //For role, the API accepts the string values "user", "manager", and "admin"
                Username = username,
                Password = password     //The password can be set as temporary by using the "change password" API call
            };

            //This is our API request for creating a new user
            var createUserRequest = new Skylight.Api.Authentication.V1.UsersRequests.CreateUserRequest(newUserBody);

            //Execute the request
            var result = await skyManager.ApiClient.ExecuteRequestAsync(createUserRequest);

            //Handle the resulting status code appropriately
            switch(result.StatusCode) {
                case System.Net.HttpStatusCode.Forbidden:
                    Console.Error.WriteLine("Error creating user: Permission forbidden.");
                    throw new Exception("Error creating user.");
                case System.Net.HttpStatusCode.Unauthorized:
                    Console.Error.WriteLine("Error creating user: Method call was unauthenticated.");
                    throw new Exception("Error creating user.");
                case System.Net.HttpStatusCode.Created:
                    Console.WriteLine("User successfully created.");
                    break;
                default:
                    Console.Error.WriteLine("Unhandled user creation status code: " + result.StatusCode);
                    throw new Exception("Error creating user.");
            }
        }

    static async Task removeAllAssignmentsForUser(string userId) {
            //First, get a list of all the user's assignments.
            var assignmentsRequest = new Skylight.Api.Assignments.V1.AssignmentRequests.GetAssignmentsRequest();
            //Make sure we only get assignments for our user
            assignmentsRequest.AddUserIdsQuery(userId);

            var result = await skyManager.ApiClient.ExecuteRequestAsync(assignmentsRequest);
            
            //Handle the resulting status code appropriately
            switch(result.StatusCode) {
                case System.Net.HttpStatusCode.Forbidden:
                    Console.Error.WriteLine("Error retrieving assignments for user: Permission forbidden.");
                    throw new Exception("Error retrieving assignments for user.");
                case System.Net.HttpStatusCode.Unauthorized:
                    Console.Error.WriteLine("Error retrieving assignments for user: Method call was unauthenticated.");
                    throw new Exception("Error retrieving assignments for user.");
                case System.Net.HttpStatusCode.NotFound:
                    Console.Error.WriteLine("Error retrieving assignments for user: User not found.");
                    throw new Exception("Error retrieving assignments for user.");
                case System.Net.HttpStatusCode.OK:
                    Console.WriteLine("User assignments successfully retrieved");
                    break;
                default:
                    Console.Error.WriteLine("Unhandled user creation status code: " + result.StatusCode);
                    throw new Exception("Error retrieving assignments for user.");
            }

            foreach(var assignment in result.Content) {
                await DeleteAssignment(assignment.Id);
            }
        }

        //@skydocs.start(assignments.delete)
        static async Task DeleteAssignment(string assignmentId, bool shouldPurge = false) {
            var deleteRequestBody = new Skylight.Api.Assignments.V1.AssignmentRequests.DeleteAssignmentRequest(assignmentId);
            /*
                This next line is optional. If the purge parameter is added and is set to true, the assignment will be purged forever.
                Otherwise the default action will be for the assignment to be archived.
            */
            deleteRequestBody.AddPurgeQuery(shouldPurge);
            var result = await skyManager.ApiClient.ExecuteRequestAsync(deleteRequestBody);
            
            //Handle the resulting status code appropriately
            switch(result.StatusCode) {
                case System.Net.HttpStatusCode.Forbidden:
                    Console.Error.WriteLine("Error deleting assignment: Permission forbidden.");
                    throw new Exception("Error deleting assignment.");
                case System.Net.HttpStatusCode.Unauthorized:
                    Console.Error.WriteLine("Error deleting assignment: Method call was unauthenticated.");
                    throw new Exception("Error deleting assignment.");
                case System.Net.HttpStatusCode.NotFound:
                    Console.Error.WriteLine("Error deleting assignment: Assignment not found.");
                    throw new Exception("Error deleting assignment.");
                case System.Net.HttpStatusCode.OK:
                    Console.WriteLine("Assignment successfully deleted");
                    break;
                default:
                    Console.Error.WriteLine("Unhandled user creation status code: " + result.StatusCode);
                    throw new Exception("Error deleting assignment.");
            }
        }
        //@skydocs.end()

        static AssignmentNew createAssignment() {

            //Create the assignment body
            var assignment = new AssignmentNew
            {
                Description = "This is an assignment for testing extensions.",
                IntegrationId = skyManager.IntegrationId, //It's important for us to specify the integrationId here, in order for us to receive events related to this assignment (like `Mark Complete`)
                Name = "Extension Test Assignment " + AssignmentCount
            };

            //Increment our assignment count
            AssignmentCount += 1;
            
            var sequence = createSequence();

            //Add the sequence to the assignment. If we had more sequences, we would add them here.
            assignment.Sequences = new System.Collections.Generic.List<SequenceNew>
            {
                sequence
            };

            //Set this sequence to be the root sequence
            assignment.RootSequence = sequence.Id;

            return assignment;
        }
        
        static SequenceNew createSequence() {
            
            var sequence = new SequenceNew
            {
                Id = "sequence1",
                ViewMode = ViewMode.Native //This is the default view mode and will generally be used
            };

            // create cards 
            CardNew labelCard = createLabelCard("read me");
            CardNew multChoiceCard = createMultipleChoiceCard("click me"); 
            CardNew labelCard2 = createLabelCard("ignore me"); 
            CardNew markCompleteCard = createMarkCompleteCard(); 

            // set positions 
            labelCard.Position = 1; 
            multChoiceCard.Position = 2; 
            labelCard2.Position = 3; 
            markCompleteCard.Position = 4; 

            // add all the cards to the list of cards
            sequence.Cards = new System.Collections.Generic.List<CardNew> {
                labelCard, multChoiceCard, labelCard2, markCompleteCard
            }; 
            return sequence; 
        }

        static CardNew createLabelCard(string label) {
            return new CardNew
            {
                Label = label,
                Size = 1, //Size can be 1, 2, or 3 and determines how much of the screen a card takes up (3 being fullscreen)
                Layout = new LayoutText(),
                Selectable = true //We have to make sure this card is selectable so that the user can view it
            };
        }

        static CardNew createMultipleChoiceCard(string label) {

            // create the choice objects 
            Choice choice1 = new Choice(); 
            choice1.Label = "option 1";
            choice1.Position = 1;
            Choice choice2 = new Choice(); 
            choice2.Label = "option 2"; 
            choice2.Position = 2; 
            Choice choice3 = new Choice();
            choice3.Label = "option 3"; 
            choice3.Position = 3; 
            Dictionary<String, Choice> choices = new Dictionary<String,Choice>(); 
            choices.Add("id1", choice1);
            choices.Add("id2", choice2); 
            choices.Add("id3", choice3); 
            return new CardNew {
                Label = label, 
                Size = 1, 
                Layout = new LayoutText(), 
                Selectable = true, 
                Component = new ComponentDecision {
                    Choices = choices 
                }
            }; 
        }

        static CardNew createPhotoCaptureCard() {
            return new CardNew
            {
                Label = "Take Photo",
                Size = 1, 
                Layout = new LayoutImage
                {
                    Uri = "resource://image/ic_media_camera_01"
                },
                Component = new ComponentCapturePhoto(),
                Selectable = true //We have to make sure this card is selectable so that the user can take a photo
            };
        }

        static CardNew createMarkCompleteCard() {
            return new CardNew
            {
                Label = "Mark Complete",
                Size = 1, 
                Layout = new LayoutImage
                {
                    Uri = "resource://image/ic_state_complete_01"
                },
                Component = new ComponentCompletion() 
                {
                    Done = new DoneOnSelect()
                },
                Selectable = true //We have to make sure this card is selectable so that the user can mark the assignment as complete
            };
        }
        
        static async Task assignToUser(AssignmentNew assignment, string userId) {
            //Set the assignment's user
            assignment.AssignedTo = userId;

            //Create the request for the assignment creation API
            var request = new Skylight.Api.Assignments.V1.AssignmentRequests.CreateAssignmentRequest(assignment);

            //Now, the magic happens -- we make a single API call to create this assignment, sequences/cards and all.
            var result = await skyManager.ApiClient.ExecuteRequestAsync(request);
            assign = result.Content; 
            
            //Handle the resulting status code appropriately
            switch(result.StatusCode) {
                case System.Net.HttpStatusCode.Forbidden:
                    Console.Error.WriteLine("Error creating assignment: Permission forbidden.");
                    throw new Exception("Error creating assignment.");
                case System.Net.HttpStatusCode.Unauthorized:
                    Console.Error.WriteLine("Error creating assignment: Method call was unauthenticated.");
                    throw new Exception("Error creating assignment.");
                case System.Net.HttpStatusCode.Created:
                    Console.WriteLine("Assignment successfully created.");
                    break;
                default:
                    Console.Error.WriteLine("Unhandled assignment creation status code: " + result.StatusCode);
                    throw new Exception("Error creating assignment.");
            }
        }
    }
}