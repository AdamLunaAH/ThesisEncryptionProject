# SeidoSwStack-with-chat-sessions-for-azure
The Maui-chat is in the solution but is commented out in the slnx-file, beacuse the solution will crash when building the app. Issue seems to be around JWTBearer, Newtonsoft and/or Maui not having support for AspNetCore (even when it doesnt use AspNetCore, but is in layers below the Maui client).
