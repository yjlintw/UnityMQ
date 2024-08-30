const ws = new WebSocket('ws://localhost:3000');

ws.onopen = () => {
    console.log('Connected to WebSocket server');
};

ws.onmessage = (event) => {
    const message = JSON.parse(event.data);
    if (message.type === 'allTopics') {
        displayAllTopics(message.data);
    } else if (message.type === 'generalUpdate') {
        updateGeneralTopic(message.topic, message.data);
    } else if (message.type === 'allStatusData') {
        displayAllStatusData(message.data);
    } else if (message.type === 'statusUpdate') {
        updateStatusDisplay(message.guid, message.data);
    }
};

ws.onclose = () => {
    console.log('WebSocket connection closed');
};

// Set to track fields that are currently being edited
const editingFields = new Set();

// Utility function to create IDs consistently
function createId(guid, instanceID, fieldName, suffix = '') {
    return `input-${guid}-${instanceID}-${fieldName}${suffix ? '-' + suffix : ''}`;
}

// Utility function to add focus and blur event listeners to an input element
function addFocusBlurListeners(inputElement, fieldName) {
    inputElement.addEventListener('focus', () => {
        editingFields.add(fieldName);
    });

    inputElement.addEventListener('blur', () => {
        editingFields.delete(fieldName);
    });
}

function displayAllTopics(allTopics) {
    const generalTopicContainer = document.getElementById('general-topic-entries');
    generalTopicContainer.innerHTML = ''; // Clear existing entries

    for (const [topic, values] of Object.entries(allTopics)) {
        addGeneralTopicEntry(topic, values);
    }
}

function addGeneralTopicEntry(topic, values) {
    const generalTopicContainer = document.getElementById('general-topic-entries');
    const row = document.createElement('tr');
    row.id = `row-${topic}`;

    const topicCell = document.createElement('td');
    topicCell.textContent = topic;
    row.appendChild(topicCell);

    const valueCell = document.createElement('td');
    valueCell.innerHTML = `<pre>${JSON.stringify(values, null, 2)}</pre>`;
    row.appendChild(valueCell);

    generalTopicContainer.appendChild(row);
}

function updateGeneralTopic(topic, values) {
    const row = document.getElementById(`row-${topic}`);
    if (row) {
        const valueCell = row.cells[1];
        valueCell.innerHTML = `<pre>${JSON.stringify(values, null, 2)}</pre>`;
    } else {
        addGeneralTopicEntry(topic, values);
    }
}

function displayAllStatusData(allStatusData) {
    const statusContainer = document.getElementById('status-container');
    statusContainer.innerHTML = ''; // Clear existing entries

    for (const [guid, instances] of Object.entries(allStatusData)) {
        for (const [instanceID, fields] of Object.entries(instances)) {
            addStatusSection(guid, instanceID, fields);
        }
    }
}

function addStatusSection(guid, instanceID, fields) {
    const statusContainer = document.getElementById('status-container');

    const sectionDiv = document.createElement('div');
    sectionDiv.id = `section-${guid}-${instanceID}`;
    sectionDiv.innerHTML = `<h3>GUID: ${guid}, Instance ID: ${instanceID}</h3>`;

    const table = document.createElement('table');
    table.innerHTML = `
        <thead>
            <tr>
                <th>Field Name</th>
                <th>Value</th>
                <th>Action</th>
            </tr>
        </thead>
        <tbody id="tbody-${guid}-${instanceID}">
            ${Object.entries(fields).map(([fieldName, value]) => `
                <tr>
                    <td>${fieldName}</td>
                    <td>${generateInputField(guid, instanceID, fieldName, value)}</td>
                    <td><button onclick="sendUpdateCommand('${guid}', '${instanceID}', '${fieldName}')">Update</button></td>
                </tr>
            `).join('')}
        </tbody>
    `;

    sectionDiv.appendChild(table);
    statusContainer.appendChild(sectionDiv);
}

function generateInputField(guid, instanceID, fieldName, value) {
    // Parse JSON string if value is a stringified JSON
    if (typeof value === 'string') {
        try {
            value = JSON.parse(value);
        } catch (e) {
            console.error("Failed to parse JSON:", e);
        }
    }

    if (value !== null && typeof value === 'object') {
        if (value.hasOwnProperty('x') && value.hasOwnProperty('y') && value.hasOwnProperty('z')) {
            return `
                X: <input type="number" step="0.01" class="vector-input" id="${createId(guid, instanceID, fieldName, 'x')}" value="${value.x}" onfocus="editingFields.add('${guid}.${instanceID}.${fieldName}')" onblur="editingFields.delete('${guid}.${instanceID}.${fieldName}')" onkeydown="if(event.key === 'Enter') sendUpdateCommand('${guid}', '${instanceID}', '${fieldName}')">
                Y: <input type="number" step="0.01" class="vector-input" id="${createId(guid, instanceID, fieldName, 'y')}" value="${value.y}" onfocus="editingFields.add('${guid}.${instanceID}.${fieldName}')" onblur="editingFields.delete('${guid}.${instanceID}.${fieldName}')" onkeydown="if(event.key === 'Enter') sendUpdateCommand('${guid}', '${instanceID}', '${fieldName}')">
                Z: <input type="number" step="0.01" class="vector-input" id="${createId(guid, instanceID, fieldName, 'z')}" value="${value.z}" onfocus="editingFields.add('${guid}.${instanceID}.${fieldName}')" onblur="editingFields.delete('${guid}.${instanceID}.${fieldName}')" onkeydown="if(event.key === 'Enter') sendUpdateCommand('${guid}', '${instanceID}', '${fieldName}')">
            `;
        } else if (value.hasOwnProperty('r') && value.hasOwnProperty('g') && value.hasOwnProperty('b') && value.hasOwnProperty('a')) {
            return `
                <input type="color" id="${createId(guid, instanceID, fieldName, 'color-picker')}" value="${rgbToHex(value.r, value.g, value.b)}" onchange="syncColorPicker(this, '${guid}', '${instanceID}', '${fieldName}')">
                R: <input type="number" step="0.01" class="color-input" id="${createId(guid, instanceID, fieldName, 'r')}" value="${value.r}" onfocus="editingFields.add('${guid}.${instanceID}.${fieldName}')" onblur="editingFields.delete('${guid}.${instanceID}.${fieldName}')" onkeydown="if(event.key === 'Enter') sendUpdateCommand('${guid}', '${instanceID}', '${fieldName}')">
                G: <input type="number" step="0.01" class="color-input" id="${createId(guid, instanceID, fieldName, 'g')}" value="${value.g}" onfocus="editingFields.add('${guid}.${instanceID}.${fieldName}')" onblur="editingFields.delete('${guid}.${instanceID}.${fieldName}')" onkeydown="if(event.key === 'Enter') sendUpdateCommand('${guid}', '${instanceID}', '${fieldName}')">
                B: <input type="number" step="0.01" class="color-input" id="${createId(guid, instanceID, fieldName, 'b')}" value="${value.b}" onfocus="editingFields.add('${guid}.${instanceID}.${fieldName}')" onblur="editingFields.delete('${guid}.${instanceID}.${fieldName}')" onkeydown="if(event.key === 'Enter') sendUpdateCommand('${guid}', '${instanceID}', '${fieldName}')">
                A: <input type="number" step="0.01" class="color-input" id="${createId(guid, instanceID, fieldName, 'a')}" value="${value.a}" onfocus="editingFields.add('${guid}.${instanceID}.${fieldName}')" onblur="editingFields.delete('${guid}.${instanceID}.${fieldName}')" onkeydown="if(event.key === 'Enter') sendUpdateCommand('${guid}', '${instanceID}', '${fieldName}')">
            `;
        }
    }
    return `<input type="text" class="status-input" id="${createId(guid, instanceID, fieldName)}" value="${value}" onfocus="editingFields.add('${guid}.${instanceID}.${fieldName}')" onblur="editingFields.delete('${guid}.${instanceID}.${fieldName}')" onkeydown="if(event.key === 'Enter') sendUpdateCommand('${guid}', '${instanceID}', '${fieldName}')">`;
}

function updateStatusDisplay(guid, updatedFields) {
    for (const [key, value] of Object.entries(updatedFields)) {
        const [instanceID, fieldName] = key.split('.');
        const combinedKey = `${guid}.${instanceID}.${fieldName}`;

        if (editingFields.has(combinedKey)) {
            // Skip updating fields that are currently being edited
            continue;
        }

        if (typeof value === 'object' && value !== null) {
            if (value.hasOwnProperty('x') && value.hasOwnProperty('y') && value.hasOwnProperty('z')) {
                const xInput = document.getElementById(createId(guid, instanceID, fieldName, 'x'));
                const yInput = document.getElementById(createId(guid, instanceID, fieldName, 'y'));
                const zInput = document.getElementById(createId(guid, instanceID, fieldName, 'z'));
                if (xInput && yInput && zInput) {
                    xInput.value = value.x;
                    yInput.value = value.y;
                    zInput.value = value.z;
                }
            } else if (value.hasOwnProperty('r') && value.hasOwnProperty('g') && value.hasOwnProperty('b') && value.hasOwnProperty('a')) {
                const rInput = document.getElementById(createId(guid, instanceID, fieldName, 'r'));
                const gInput = document.getElementById(createId(guid, instanceID, fieldName, 'g'));
                const bInput = document.getElementById(createId(guid, instanceID, fieldName, 'b'));
                const aInput = document.getElementById(createId(guid, instanceID, fieldName, 'a'));
                const colorPicker = document.getElementById(`color-picker-${guid}-${instanceID}-${fieldName}`);
                if (rInput && gInput && bInput && aInput && colorPicker) {
                    rInput.value = value.r;
                    gInput.value = value.g;
                    bInput.value = value.b;
                    aInput.value = value.a;
                    colorPicker.value = rgbToHex(value.r, value.g, value.b);
                }
            }
        } else {
            const inputElement = document.getElementById(createId(guid, instanceID, fieldName));
            if (inputElement) {
                inputElement.value = value;
            }
        }
    }
}

function sendUpdateCommand(guid, instanceID, fieldName) {
    const inputElements = document.querySelectorAll(`[id^="${createId(guid, instanceID, fieldName)}"]`);
    let newValue = {};

    inputElements.forEach(input => {
        const part = input.id.split('-').pop();
        newValue[part] = parseFloat(input.value);
    });

    if (Object.keys(newValue).length === 1) {
        newValue = newValue[Object.keys(newValue)[0]];
    }

    const command = {
        Topic: `command/${guid}/${instanceID}/${fieldName}`,
        Timestamp: new Date().toISOString(),
        Values: { [fieldName]: newValue },
        IsPersistent: false
    };

    ws.send(JSON.stringify(command));
    console.log(`Sent command: ${JSON.stringify(command)}`);
}

function rgbToHex(r, g, b) {
    return "#" + ((1 << 24) + (Math.floor(r * 255) << 16) + (Math.floor(g * 255) << 8) + Math.floor(b * 255)).toString(16).slice(1);
}

function syncColorPicker(picker, guid, instanceID, fieldName) {
    const color = picker.value;
    const r = parseInt(color.substr(1, 2), 16) / 255;
    const g = parseInt(color.substr(3, 2), 16) / 255;
    const b = parseInt(color.substr(5, 2), 16) / 255;

    document.getElementById(createId(guid, instanceID, fieldName, 'r')).value = r;
    document.getElementById(createId(guid, instanceID, fieldName, 'g')).value = g;
    document.getElementById(createId(guid, instanceID, fieldName, 'b')).value = b;
    sendUpdateCommand(guid, instanceID, fieldName);
}