
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

namespace HelloWorld {
    /*
        INFO: Throughout this example, there are comments that begin with @skydocs -- 
        these are tags used by the Skylight Developer Portal and are not necessary for
        this example to function.
    */
    class Program
    {
        public static Manager SkyManager;
        public static string UserId;
        private readonly static string MARK_COMPLETE_TAG = "seq1completion"; //Tags can only be up to 20 characters long
        private readonly static string PHOTO_CAPTURE_TAG = "seq1photo"; //Tags can only be up to 20 characters long
        private readonly static string FILES_DIRECTORY = Path.Join(".", "tmp"); //We'll download our uploaded files to a folder called "tmp"
        private static int AssignmentCount = 0;
        static async Task Main(string[] args)
        {
            try {
                //Create our manager and point it to our credentials file
                //We leave the parameter blank, so that it looks for the `credentials.json` in the root directory.
                SkyManager = new Manager();
            } catch { return; }
            
            /* In this Hello World example, we:
                0. Subscribe to Skylight events, to listen for `Mark Complete` events. When one of these events is detected, we'll archive that assignment and create a new one for the same user
                1. Prompt for a username on the command line
                    a. If a user with that username exists, prompt whether we should go ahead and use that user for the rest of this Hello World.
                    b. If a user with that username doesn't exist, prompt for a password and then create that user.
                2. Remove all assignments from the user.
                3. Create a simple assignment with one sequence that has three cards, including:
                    a. A label card that says "Hello World"
                    b. A photo capture card that, when a photo is captured, this extension will download the photo
                    c. A mark complete card that will emit the mark complete event when tapped, which will trigger our listener from the earlier step.
                4. Assign the assignment to the user
                5. Keep the program alive by using SpinWait.
            */
        
            //0. Subscribe
            await SubscribeToSkylightEvents();

            //1. Get the userId of the Hello World user based on the command prompt input
            UserId = await GetHelloWorldUserId();
            if (UserId == null) {
                Console.WriteLine("Exiting Skylight Hello World extension.");
                return;
            }

            //2. Remove all assignments
            await RemoveAllAssignmentsForUser(UserId);
            
            //3. Create a new assignment
            //It is important to note that this next call doesn't reach out to the servers -- the assignment information is created in one fell swoop in memory first.
            var assignmentBody = CreateAssignment();

            //4. Assign the assignment to the user
            await AssignToUser(assignmentBody, UserId);

            //Let the user know that we've completed our work here and that they should continue on a device.
            Console.WriteLine("Congratulations! Your user is now set up with a the Hello World assignment. Log in to the domain `" + SkyManager.Domain + "` using that username and password in Skylight Web to view this assignment.");
            
            //5. Wait forever (at least, until the program is stopped)
            SpinWait.SpinUntil(() => false);

        }

        //In this method, we subscribe to Skylight events. In particular for this example, we're most interested in listening for the `Mark Complete` event.
        static async Task SubscribeToSkylightEvents() {
            SkyManager.MessagingClient.Connected += (object sender, MqttClientConnectedEventArgs args) => {
                Console.WriteLine("Skylight MQTT client connected.");
            };

            SkyManager.MessagingClient.Disconnected += (object sender, MqttClientDisconnectedEventArgs args) => {
                Console.WriteLine("Skylight MQTT client disconnected.");
                Console.WriteLine(args.Exception.Message);
            };

            SkyManager.MessagingClient.TopicSubscribed += (object sender, MqttMsgSubscribedEventArgs args) => {
                Console.WriteLine("Skylight MQTT client subscribed to: " + args.Topic);
            };

            SkyManager.MessagingClient.MessageReceived += (object sender, MessageReceivedEventArgs args) => {
                //Console.WriteLine("Skylight Message received on topic " + args.Topic + " " + args.Message); //Uncomment this for more verbose logging of Skylight event messages
            };

            SkyManager.MessagingClient.CardUpdated += async (object sender, CardUpdatedEventArgs args) => { await CardUpdated(sender, args); };


            await SkyManager.StartListening();
        }

        //@skydocs.start(mqtt.cardupdated.cardtags)
        //@skydocs.start(media.download)
        static async Task CardUpdated(object sender, CardUpdatedEventArgs args) {
            /*
                There are many ways we can handle this event. We could:
                - use the cardId to determine what action to take
                - pull down the card and:
                    - look at its tags to determine what to do
                    - look at the card type and decide what to do

                For this example, we'll pull down the card and look at its tags (specifically, we'll make sure it has the MARK_COMPLETE_TAG tag that we used earlier)
            */
            var result = await SkyManager.ApiClient.ExecuteRequestAsync(new Skylight.Api.Assignments.V1.CardRequests.GetCardRequest(args.AssignmentId, args.SequenceId, args.CardId));

            //Handle the resulting status code appropriately
            switch(result.StatusCode) {
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
            if(cardInfo.Tags.Contains(MARK_COMPLETE_TAG)) {
                //If the card's tags includes a MARK_COMPLETE_TAG, then we'll reassign the assignment
                Console.WriteLine("Card tags contains MARK_COMPLETE_TAG");

                //If the card isn't marked complete, return
                if(!cardInfo.IsDone.HasValue || !cardInfo.IsDone.Value)return;
                Console.WriteLine("Card is marked complete");

                //Otherwise, reassign the assignment
                await DeleteAssignment(args.AssignmentId);
                var assignmentBody = CreateAssignment();
                await AssignToUser(assignmentBody, UserId);
            } else if(cardInfo.Tags.Contains(PHOTO_CAPTURE_TAG)) {
                //If the card's tags includes a PHOTO_CAPTURE_TAG, we'll download the photo that was captured
                var photoCaptureComponent = (ComponentCapturePhoto)cardInfo.Component;
                Console.WriteLine(cardInfo.ToJson());
                var photoCaptureURIs = photoCaptureComponent.Captures;
                foreach(var photoCaptureURI in photoCaptureURIs) {
                    await DownloadPhoto(photoCaptureURI);
                    Console.WriteLine(photoCaptureURI);
                }
            }
        }
        //@skydocs.end()

        //@skydocs.start(media.download)
        static async Task DownloadPhoto(string uri) { //This uri can be, for example, a URI from a capture photo component's captures field.
            //First, get the file metadata so we have some more information about the file
            string[] splitString = uri.Split("/");
            var photoId = splitString[splitString.Length-2];
            var metadataResult = await SkyManager.ApiClient.ExecuteRequestAsync(new Skylight.Api.Media.V3.FilesRequests.GetFileRequest(photoId));

            //Handle the resulting status code appropriately
            switch(metadataResult.StatusCode) {
                case System.Net.HttpStatusCode.Forbidden:
                    Console.Error.WriteLine("Error retrieving file metadata: Permission forbidden.");
                    throw new Exception("Error retrieving file metadata.");
                case System.Net.HttpStatusCode.Unauthorized:
                    Console.Error.WriteLine("Error retrieving file metadata: Method call was unauthenticated.");
                    throw new Exception("Error retrieving file metadata.");
                case System.Net.HttpStatusCode.NotFound:
                    Console.Error.WriteLine("Error retrieving file metadata: File not found.");
                    throw new Exception("Error retrieving file metadata.");
                case System.Net.HttpStatusCode.OK:
                    Console.WriteLine("File metadata successfully retrieved");
                    break;
                default:
                    Console.Error.WriteLine("Unhandled user creation status code: " + metadataResult.StatusCode);
                    throw new Exception("Error retrieving file metadata.");
            }

            //Create the tmp folder if it doesn't exist
            if(!Directory.Exists(FILES_DIRECTORY)){
                Directory.CreateDirectory(FILES_DIRECTORY);
            }

            var photoData = metadataResult.Content;
            var filePath = Path.Join(FILES_DIRECTORY, photoData.Filename);

            //If the file exists, don't re-download
            if(File.Exists(filePath))return;
            var fileResult = await SkyManager.ApiClient.ExecuteRequestAsync(new Skylight.Api.Media.V3.FilesRequests.GetContentRequest(photoId));
            
            //Handle the resulting status code appropriately
            switch(fileResult.StatusCode) {
                case System.Net.HttpStatusCode.Forbidden:
                    Console.Error.WriteLine("Error retrieving file: Permission forbidden.");
                    throw new Exception("Error retrieving file.");
                case System.Net.HttpStatusCode.Unauthorized:
                    Console.Error.WriteLine("Error retrieving file: Method call was unauthenticated.");
                    throw new Exception("Error retrieving file.");
                case System.Net.HttpStatusCode.NotFound:
                    Console.Error.WriteLine("Error retrieving file: File not found.");
                    throw new Exception("Error retrieving file.");
                case System.Net.HttpStatusCode.OK:
                    Console.WriteLine("File successfully downloaded");
                    break;
                default:
                    Console.Error.WriteLine("Unhandled user creation status code: " + metadataResult.StatusCode);
                    throw new Exception("Error retrieving file.");
            }

            var fileBytes = fileResult.Content;
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                fs.Write(fileBytes, 0, fileBytes.Length);
            }
            Console.WriteLine("Photo downloaded to folder \"" + FILES_DIRECTORY + "\".");
            Console.WriteLine("Photo has the following tags in its metadata: ");
            foreach(string tag in photoData.Tags) {
                Console.WriteLine("- " + tag);
            }

        }
        //@skydocs.end()

        /*
            This method will prompt for a username to use for this Hello World and then:
                0. If the user exists, ask if we want to continue the Hello World with that user
                1. Otherwise, prompt for a password and create the user
        */
        static async Task<string> GetHelloWorldUserId() {

            Console.WriteLine("Welcome to the Skylight Hello World extension! Please enter a username to use for this Hello World:");
            string username = Console.ReadLine();

            string userId = await GetUserIdForUsername(username);
            
            //If userId isn't null, then that user already exists -- see if we actually want to use that user
            if(userId != null) {
                Console.WriteLine("That user currently exists in your domain. Would you like to continue this Hello World with that user? [type 'yes' or 'no']\n(IMPORTANT: This Hello World will delete all assignments from this user.)");
                string choice = Console.ReadLine();

                //Make sure the user explicitly specifies 'yes' or 'no'
                while(!(choice.ToLower().Equals("no") || choice.ToLower().Equals("yes"))) {
                    Console.WriteLine("Please type 'yes' or 'no'.");
                    choice = Console.ReadLine();
                }
                if(choice.ToLower().Equals("no"))return null;
                return userId;
            }

            //Otherwise, prompt for a password and create the user
            string password = GetPassword();
            await CreateUser("Hello", "World", Role.User, username, password);
            userId = await GetUserIdForUsername(username);
            
            //At this point, if userId is still null, we've thrown an exception.
            return userId;
        }

        //This code was pulled in from: https://social.msdn.microsoft.com/Forums/vstudio/en-US/455eefeb-7624-4d81-b921-30f19891b2a7/any-way-to-prompt-user-for-a-password-and-then-hide-it?forum=csharpgeneral
        static string GetPassword() { 
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
        
        //As the name suggests, this method will get the user ID for a given username -- or will return null, if the user doesn't exist
        static async Task<string> GetUserIdForUsername(string username) {
            //Create an API request for retrieving all users
            var getUsersRequest = new Skylight.Api.Authentication.V1.UsersRequests.GetUsersRequest();

            //Execute the API request
            var result = await SkyManager.ApiClient.ExecuteRequestAsync(getUsersRequest);

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

        static async Task CreateUser(string first, string last, Role role, string username, string password) {
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
            var result = await SkyManager.ApiClient.ExecuteRequestAsync(createUserRequest);

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
        
        static async Task RemoveAllAssignmentsForUser(string userId) {
            //First, get a list of all the user's assignments.
            var assignmentsRequest = new Skylight.Api.Assignments.V1.AssignmentRequests.GetAssignmentsRequest();
            //Make sure we only get assignments for our user
            assignmentsRequest.AddUserIdsQuery(userId);

            var result = await SkyManager.ApiClient.ExecuteRequestAsync(assignmentsRequest);
            
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
            var result = await SkyManager.ApiClient.ExecuteRequestAsync(deleteRequestBody);
            
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

        static AssignmentNew CreateAssignment() {

            //Create the assignment body
            var assignment = new AssignmentNew
            {
                Description = "This is an assignment created by the SDK Hello World.",
                IntegrationId = SkyManager.IntegrationId, //It's important for us to specify the integrationId here, in order for us to receive events related to this assignment (like `Mark Complete`)
                Name = "SDK Hello World Assignment " + AssignmentCount
            };

            //Increment our assignment count
            AssignmentCount += 1;
            
            var sequence = CreateSequence();

            //Add the sequence to the assignment. If we had more sequences, we would add them here.
            assignment.Sequences = new System.Collections.Generic.List<SequenceNew>
            {
                sequence
            };

            //Set this sequence to be the root sequence
            assignment.RootSequence = sequence.Id;

            return assignment;
        }
        
        static SequenceNew CreateSequence() {
            
            var sequence = new SequenceNew
            {
                Id = "sequence1",
                ViewMode = ViewMode.Native //This is the default view mode and will generally be used
            };

            CardNew labelHelloCard = CreateLabelCard("Hello World");
            labelHelloCard.Position = 1;
            labelHelloCard.Id = "card1"; //This could be a UUID -- as long as it's unique within the sequence, we're good
            
            //@skydocs.start(cards.tags)
            CardNew photoCaptureCard = CreatePhotoCaptureCard();
            photoCaptureCard.Position = 2;
            photoCaptureCard.Id = "card2";

            //We'll add a tag to the photo capture card so we can identify it when we handle the card updated event
            //An IMPORTANT NOTE is that any tags on this photo capture card will be added to any photos captured by this card, in the photo's metadata
            photoCaptureCard.Tags = new System.Collections.Generic.List<string>
            {
                PHOTO_CAPTURE_TAG
            };

            CardNew markCompleteCard = CreateMarkCompleteCard();
            markCompleteCard.Position = 3;
            markCompleteCard.Id = "card3";

            //We'll add a tag to the mark complete card so we can look for it later when we handle the event
            markCompleteCard.Tags = new System.Collections.Generic.List<string> 
            {
                MARK_COMPLETE_TAG
            };
            //@skydocs.end()

            //Set the cards to live in the sequence. We could create more cards and add them in a similar manner
            sequence.Cards = new System.Collections.Generic.List<CardNew>
            {
                labelHelloCard
                , photoCaptureCard
                , markCompleteCard
            };

            return sequence;
        }

        static CardNew CreateLabelCard(string label) {
            return new CardNew
            {
                Label = label,
                Size = 1, //Size can be 1, 2, or 3 and determines how much of the screen a card takes up (3 being fullscreen)
                Layout = new LayoutText(),
                Selectable = true //We have to make sure this card is selectable so that the user can view it
            };
        }

        static CardNew CreatePhotoCaptureCard() {
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

        static CardNew CreateMarkCompleteCard() {
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
        
        static async Task AssignToUser(AssignmentNew assignment, string userId) {
            //Set the assignment's user
            assignment.AssignedTo = userId;

            //Create the request for the assignment creation API
            var request = new Skylight.Api.Assignments.V1.AssignmentRequests.CreateAssignmentRequest(assignment);

            //Now, the magic happens -- we make a single API call to create this assignment, sequences/cards and all.
            var result = await SkyManager.ApiClient.ExecuteRequestAsync(request);
            
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