#define numOfMotors 4

int pTol = 10; // tolerance for pressure controller
int pSensors[] = {2, 1, 0}; // Analog-In pins for pressure sensors
int solenoids[] = {2, 3}; // digital pins for solenoid MOSFET gates
int pSet[] = {0, 0}; // 10 bit number to set pressure

// Stepper Stuff
const int motorPin[numOfMotors][4] = 
  { {22, 23, 24, 25}, {28, 29, 30, 31}, {34, 35, 36, 37}, {40, 41, 42, 43} }; // row is motor, column is pin
int stepperState[numOfMotors] = {0}; // state machine for stepper motors
int steps[numOfMotors] = {0}; // steps for steppers to move: 0 means doesn't have to move
int requestedSteps[numOfMotors]; // requests to update "steps", updated by Serial
bool newRequest[numOfMotors] = {false}; // keeps track if request was fulfilled 
long lastStepTime[numOfMotors] = {0}; // to space out the steps
#define stepPause 20 // ms to wait between steps (basically sets stepper speed)

// Serial stuff
int parseState = 0;
char commandType = 0; // the letter to say which function to call
char buf[15];
int idx  = 0; // buffer index
int valuesArrayIndex = 0;
int valuesArray[5] = {0}; // numbers to be passed to functions (e.g. sensor #, fan percent, temperature, pressure)


void setup() 
{
  for (int j = 0; j < sizeof(solenoids); j++)
  {
  pinMode(solenoids[j], OUTPUT);
  digitalWrite(solenoids[j], LOW);
  }

  for (int motor = 0; motor < numOfMotors; motor++) 
  {
    pinMode(motorPin[motor][0], OUTPUT);
    pinMode(motorPin[motor][1], OUTPUT);
    pinMode(motorPin[motor][2], OUTPUT);
    pinMode(motorPin[motor][3], OUTPUT);
  }
  // set registers for timer3
  // pins 2, 3, 5 use timer3, see https://arduino-info.wikispaces.com/Timers-Arduino
  TCCR3A = B10100010;
  TCCR3B = B00010001; // 001 (no prescale)
  ICR3 = 320; // TOP = 320 for 25 kHZ
  OCR3B = 32; // pin 2 goes by OCR3B. Start fan low.
  pinMode(2, OUTPUT);
  // frequency is 16,000,000 / prescale / ICRx / 2
  // duty cycle will be OCRBxN / ICRx, look on datasheet or experiment
  
  Serial.begin(115200); // max baud rate
}

void loop()
{

UpdateCommands();
//delay(10);
//stepper(1);
//delay(10);
//stepper(0);
for (int m = 0; m < numOfMotors; m++)
{
  stepper(m);
}


}

// reads Serial
void UpdateCommands()
{
  int currentByte = Serial.read();
  if (-1 == currentByte)
  {
    return;
  }
  char currentChar = (char) currentByte;
  switch (parseState)
  {
    case 0:      // idle
      if ('%' == currentChar)
      {
        parseState = 1;
        idx = 0;
        valuesArrayIndex = 0;
      }
    break;
    
    case 1:    // saw a "%" char, so looks for command
      if ('A' == currentChar || 'M' == currentChar || 'P' == currentChar || 'F' == currentChar || 'L' == currentChar)
      {
        commandType = currentChar;
        parseState = 2;
      }
      else
      { // means got some garbage after the "%", reset
        parseState = 0;
      }
      break;
        
    case 2:    // param
      if (';' == currentChar)
      {
        buf[idx] = '\0'; // makes a null terminated string so atoi() stops there
        valuesArray[valuesArrayIndex] = atoi(buf);
        idx = 0; // got the first integer
        parseState = 0;
        doTasks(commandType, valuesArray);
      }
      else if (',' == currentChar)
      {
        buf[idx] = '\0';
        valuesArray[valuesArrayIndex] = atoi(buf); // capture the number, store in valuesArray
        valuesArrayIndex++; // moves over since it wrote a number 
        idx = 0; // resets index buffer to ready it for the next number
      }
      else
      {
        buf[idx] = currentChar; // builds up the number until "," or ";"
        idx++;
      }
      //break; // no need because it's last
  } // end switch
}

// perform the tasks requested by serial
void doTasks(char command, int values[])
{
 switch (command)
 {
  case 'A': // reads analog pin (e.g.  %A0;)
    {// remember pSensors has the pin, values[] just has the sensor #
    int sensor = values[0];
    int pin = pSensors[sensor];
    Serial.print( "\nA," + (String)sensor + "," + analogRead(pin));
    }
    break;
    
  case 'P': // sets pSet at the corresponding sensor # (e.g.  %P1,300; sets sensor # 1 to value 300)
    pSet[values[0]] = values[1]; // values = {sensor#, set point}
    break;

  case 'F': // sets fan speed percent (e.g.  %F90; sets fan to 90%)
    OCR3B = values[0] * ICR3 / 100; // pin 2 on MEGA
    break;

  case 'M': // %M(a),(b); requests motor (a) to move # of steps (b) (e.g.  %M2,-100; makes motor 2 go 100 steps clockwise)
    //int motor = values[0];
    //int reqSteps = values[1];
    //Serial.println("motor:" + (String)motor + "  reqSteps:" +(String)reqSteps);
    //requestedSteps[motor] = reqSteps;
    //newRequest[motor] = true;
    requestedSteps[values[0]] = values[1];
    newRequest[values[0]] = true;
    break;

  case 'L': // changes pressure control tolerance pTol (e.g.  %L5; sets pTol to 5)
    pTol = values[0];
    
  default: break;
 }

}

// STEPPER MOTOR
// moves "motor" by requestedSteps 
void stepper(int motor)    // requestedSteps is updated by Serial.
{
  if (millis() - lastStepTime[motor] < stepPause)
  { //Serial.println( (String)motor + " too early");
    return; // too early to step again
  }
  
  if (newRequest[motor] && 0 == stepperState[motor])
  {
      // so won't change direction before it finishes all 8 sequences
      steps[motor] = requestedSteps[motor];
      newRequest[motor] = false;
      stepperState[motor] = 1;
  }
  
  if (0 == steps[motor] && 0 == stepperState[motor]) 
  {
    return;
  }
  else if (steps[motor] < 0) // clockwise
  { 
    switch (stepperState[motor])
    {
     case 0:
       digitalWrite(motorPin[motor][3], HIGH);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][0], LOW);
       break;
     case 1:
       digitalWrite(motorPin[motor][3], HIGH);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][0], LOW);
       break;
     case 2:
       digitalWrite(motorPin[motor][3], LOW);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][0], LOW);
       break;
     case 3:
       digitalWrite(motorPin[motor][3], LOW);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][0], LOW);
       break;
     case 4 :
       digitalWrite(motorPin[motor][3], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][0], LOW);
       break;
     case 5:
       digitalWrite(motorPin[motor][3], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][0], HIGH);
       break;
     case 6:
       digitalWrite(motorPin[motor][3], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][0], HIGH);
       break;
     case 7:
       digitalWrite(motorPin[motor][3], HIGH);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][0], HIGH);
       break;
     default:
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], LOW);
       steps[motor]++; // signal that one CW step was completed
       stepperState[motor] = -1; // that way it's 0 after the ++
       break;
    }
  }
  else
  { // counterclockwise
    switch (stepperState[motor])
    {
     case 0 :
       digitalWrite(motorPin[motor][0], HIGH);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], LOW);
       break;
     case 1 :
       digitalWrite(motorPin[motor][0], HIGH);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], LOW);
       break;
     case 2 :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], LOW);
       break;
     case 3 :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], HIGH);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][3], LOW);
       break;
     case 4 :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][3], LOW);
       break;
     case 5 :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], HIGH);
       digitalWrite(motorPin[motor][3], HIGH);
       break;
     case 6 :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], HIGH);
       break;
     case 7 :
       digitalWrite(motorPin[motor][0], HIGH);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], HIGH);
       break;
     default :
       digitalWrite(motorPin[motor][0], LOW);
       digitalWrite(motorPin[motor][1], LOW);
       digitalWrite(motorPin[motor][2], LOW);
       digitalWrite(motorPin[motor][3], LOW);
       stepperState[motor] = -1; // that way it's 0 after the ++
       steps[motor]--; // signal that one CCW step was completed
       break;
    }
  }
  stepperState[motor]++;
  lastStepTime[motor] = millis();
  //Serial.println( (String)motor + " moved");
}

//// PRESSURE CONTROL VIA SOLENOIDS
//void controlPressure(int k) 
//{
//  if (analogRead(pSensors[k]) < pSet[k] - pTol) digitalWrite(solenoids[k], HIGH);
//  if (analogRead(pSensors[k]) > pSet[k] + pTol) digitalWrite(solenoids[k], LOW);
//}



