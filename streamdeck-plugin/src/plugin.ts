import streamDeck from "@elgato/streamdeck";
import { CoreConnection } from "./core-connection";
import { StateDisplay } from "./state-display";
import { DialAction } from "./actions/dial-action";
import { ButtonAction } from "./actions/button-action";
import { ResetAxisAction } from "./actions/reset-axis-action";

const connection = new CoreConnection();
const stateDisplay = new StateDisplay();
const dialAction = new DialAction(connection, stateDisplay);
dialAction.manifestId = "com.apricadabra.dial";

const buttonAction = new ButtonAction(connection);
buttonAction.manifestId = "com.apricadabra.button";

const resetAction = new ResetAxisAction(connection);
resetAction.manifestId = "com.apricadabra.reset";

connection.onStateUpdate = (axes, buttons) => {
    const changedAxes = stateDisplay.getChangedAxes(axes);
    stateDisplay.update(axes, buttons);
    dialAction.updateFeedbackForAxes(changedAxes);
};

connection.onStatusChange = (status) => {
    streamDeck.logger.info(`Core connection: ${status}`);
};

streamDeck.actions.registerAction(dialAction);
streamDeck.actions.registerAction(buttonAction);
streamDeck.actions.registerAction(resetAction);

connection.connect().catch((err) => {
    streamDeck.logger.error(`Failed to connect to core: ${err}`);
});

streamDeck.connect();
