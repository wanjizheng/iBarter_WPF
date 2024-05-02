This is a .Net application inspired by the Barter Template developed by Crippling Depression. It was the best bartering tool in the Black Desert Online. 

This is still in beta, so please help me test the application and provide feedback.

This tool uses image recognition to identify barter items in the game; then you can plan your route. To use this application, you first need to start the iBarter.exe file with admin permission, then the main interface shown below:

![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/538b032b-dbcc-42ec-b69d-29396a7d603f)

Click the "Windows" menu and then select the "BarterScanner", it will then open a new window shown below:

![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/2d0f6260-0756-42ad-ac42-71d965f785aa)

Assuming you have the barter window opened in the game, click the first button in the BaarterScanner window to start scanning. Do not move the barter window in the game during the scan process, and you will get a message (in the Log window) once the scan has been done.

![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/a4d72086-2c56-4cc3-997e-8b0d14443081)

Once you finish the scan, the identified information will show in the BarterScanner window. It is not 100% accurate at the moment, so you can edit the information if necessary.

![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/fd566e04-1e43-4aef-b2eb-dd4b3ca4d3d9)

Once you are happy with the result, click the second button in the BarterScanner window to add it to your barter plan (you can access the plan from the main window).

![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/4adfe7d8-9750-45d7-ac4e-34e59bca089d)

There are six buttons on the Planner window, from the left to the right, they are:
1. Create a new plan.
2. Save the current plan.
3. Open an existing plan.
4. Grouping.
5. Clean the current plan.
6. Finish the current plan and add the results to the storage record (Windows=>Storage Manager)

Columns in the table are:
1. Group: Group number.
2. LV: Barter item level.
3. CK: Tick this after you finish it; this will remove the item from the Map.
4. Eq.: Exchange quantity. The number of times you can barter this item.
5. No.: Required item number.
6. Location: The Islands name.
7. I1 No: Required item number.
8. I1: Required item icon.
9. Item: Required item name.
10. I2: Exchange item icon.
11. Exchange: Exchange item name.
12. I2 No.: Exchange item number.
13. Inv: How many items you currently have in your storage (will need to configure the Storage Manager to make this number right).
14. InvChange: The number of items you will get after you finish this barter.

Use the planner to plan your barter accordingly, and you will be able to see these items in the Map view which helps you decide which one do you want to barter first.
![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/d9d26329-3c19-41b2-bb77-68b825173d09)

In the map view, double-click an item will make it "CK" (remove it from the map and tick the CK box in the planner). Use the mid-button on your mouse to add an item to your Ship Cargo, which will help you work out the LT limit. Click the mid-button again will remove it from the cargo view. Right click will take you to the item record in your Planner. 

In the Ship Cargo view, click the left mouse button twice will copy the first item's name to your clickboard. Double click the right button will copy the second item's name.

