
var table = module.exports = require('azure-mobile-apps').table();

// table.read(function (context) {
//     return context.execute();
// });

// table.read.use(customMiddleware, table.operation);

var azure = require('azure-storage');

table.insert(function (context) {
    console.info(context.item.resourceName);
    console.info(context.item.containerName);
    
    // If it does not already exist, create the container 
    // with public read access for blobs. 
    var storageName = "functionimagestorage";
    var storageKey = "JOWUkh0bvU5+8d2ckUkJ92yJ/9EFHzt0th1QoiI+qKTXdSedR8O4T/dp5D2+GZlOv99Q6E0Wd7Zy9QWQ3Dfulg==";
    var blobService = azure.createBlobService(storageName, storageKey);
    
    var sharedAccessPolicy = {
      AccessPolicy: {
        Permissions: azure.BlobUtilities.SharedAccessPermissions.WRITE,
        Start: new Date(),
        Expiry: new Date(new Date().getTime() + 5 * 60 * 1000)
      },
    };
    
    console.info(sharedAccessPolicy.Start);
    console.info(sharedAccessPolicy.Expiry);
    
    // Generate the upload URL with SAS for the new image.
    var sasQueryUrl =
        blobService.generateSharedAccessSignature(context.item.containerName, context.item.resourceName, sharedAccessPolicy);
    //var host = blobService.host;
    console.info("sasQueryUrl: " + sasQueryUrl);

    // Set the query string.
    context.item.sasQueryString = sasQueryUrl;
    console.info("context.item.sasQueryString: " + context.item.sasQueryString);
    
    return context.execute();
});
