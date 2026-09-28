window.downloadFile = function (base64Data, fileName, contentType) {
    var link = document.createElement('a');
    link.download = fileName;
    link.href = "data:" + (contentType || "application/octet-stream") + ";base64," + base64Data;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

window.fileUtils = {
    saveAsFile: function (filename, base64String) {
        var link = document.createElement('a');
        link.download = filename;
        link.href = "data:application/octet-stream;base64," + base64String;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    },
    clickElement: function (elementId) {
        var element = document.getElementById(elementId);
        if (element) {
            element.click();
        }
    },
    setCookie: function (key, value, days) {
        var maxAge = days * 24 * 60 * 60;
        document.cookie = key + "=" + value + "; path=/; max-age=" + maxAge + "; SameSite=Lax; Secure";
    },
    getCookie: function (key) {
        var name = key + "=";
        var parts = document.cookie.split(';');
        for (var i = 0; i < parts.length; i++) {
            var c = parts[i].trim();
            if (c.indexOf(name) === 0) {
                return c.substring(name.length);
            }
        }
        return "";
    },
    submitForm: function (formId) {
        var form = document.getElementById(formId);
        if (form) {
            form.submit();
        }
    }
};
